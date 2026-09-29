namespace Jularr.Web.Features.Storage.FolderBrowse;

public sealed record FolderBrowseOptions(string DataRoot)
{
    /// <summary>How long one browse, check or create may take before the location counts as unavailable.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>How long the access probe of one storage root may take when the roots are listed.</summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>The most subfolders one listing returns.</summary>
    public int MaxFolders { get; init; } = 1000;
}

/// <summary>
/// The one filesystem browser of Jularr: it lists the folders of the file system as the running
/// container sees it, checks a path from the runtime user's point of view and creates folders. It is
/// never a general file manager:
/// <list type="bullet">
/// <item>Only folders are listed; no file name or file content is ever returned.</item>
/// <item>Every path must lie under a storage root (a volume mounted into the container, or the data
/// folder). The path is normalized, <c>..</c> is refused and every symbolic link on the way is
/// resolved, so a link leading out of the roots is refused as well.</item>
/// <item>Nothing runs in a shell; permissions are those of the runtime user.</item>
/// </list>
/// </summary>
public sealed class FolderBrowseService(
    IMountTable mounts,
    IDirectoryAccess access,
    FolderBrowseOptions options)
{
    /// <summary>The storage roots with their current access state, unreachable ones included.</summary>
    public async Task<IReadOnlyList<StorageRootEntry>> GetRootsAsync(CancellationToken cancellationToken)
    {
        var snapshot = mounts.GetMounts();
        var roots = DiscoverRoots(snapshot);
        var entries = await Task.WhenAll(roots.Select(async root =>
        {
            var probed = await OffloadAsync(
                () => Probe(root.RealPath, root.ReadOnlyMount),
                new PathAccess(false, false, false, root.ReadOnlyMount, Unavailable: true),
                options.ProbeTimeout,
                cancellationToken);
            return new StorageRootEntry(root.Path, root.FileSystem, probed);
        }));

        // A mount point that is not a folder (a single mounted file) is not a location to pick.
        return entries.Where(entry => entry.Access.Exists || entry.Access.Unavailable).ToArray();
    }

    public Task<BrowseResult> BrowseAsync(string? path, CancellationToken cancellationToken) =>
        OffloadAsync(
            () => Browse(path),
            new BrowseResult(PathProblem.Unavailable, null),
            options.Timeout,
            cancellationToken);

    /// <summary>
    /// What the runtime user can do at <paramref name="path"/>. With <paramref name="directoryOnly"/>
    /// a file is refused; without it a file is reported as such (a mapped completed-download path).
    /// <paramref name="otherPath"/> adds how the path relates to a second one (library and inbox).
    /// </summary>
    public Task<PathCheck> CheckAsync(
        string? path,
        bool directoryOnly,
        string? otherPath,
        CancellationToken cancellationToken) =>
        OffloadAsync(
            () => Check(path, directoryOnly, otherPath),
            new PathCheck(null, PathProblem.Unavailable, PathKind.Missing, new PathAccess(false, false, false, false, Unavailable: true)),
            options.Timeout,
            cancellationToken);

    public Task<CreateFolderResult> CreateFolderAsync(
        string? parent,
        string? name,
        CancellationToken cancellationToken) =>
        OffloadAsync(
            () => CreateFolder(parent, name),
            new CreateFolderResult(CreateFolderOutcome.Failed),
            options.Timeout,
            cancellationToken);

    /// <summary>
    /// Whether two folders are the same one or nested in each other. Links are resolved when the
    /// file system answers; otherwise the paths are compared as written.
    /// </summary>
    public Task<FolderRelation> ComparePairAsync(
        string? first,
        string? second,
        CancellationToken cancellationToken) =>
        OffloadAsync(
            () => Relate(first, second, resolveLinks: true),
            Relate(first, second, resolveLinks: false),
            options.Timeout,
            cancellationToken);

    private BrowseResult Browse(string? path)
    {
        var snapshot = mounts.GetMounts();
        var roots = DiscoverRoots(snapshot);
        var problem = Authorize(path, roots, out var normalized, out var real, out var anchor);
        if (problem != PathProblem.None)
        {
            return new BrowseResult(problem, null);
        }

        var kind = KindOf(real!);
        if (kind != PathKind.Directory)
        {
            return new BrowseResult(kind == PathKind.Missing ? PathProblem.NotFound : PathProblem.NotADirectory, null);
        }

        if (!access.CanRead(real!))
        {
            return new BrowseResult(PathProblem.NotReadable, null);
        }

        List<FolderEntry> folders;
        try
        {
            folders = ListFolders(real!, normalized, roots);
        }
        catch (UnauthorizedAccessException)
        {
            return new BrowseResult(PathProblem.NotReadable, null);
        }
        catch (IOException)
        {
            return new BrowseResult(PathProblem.Unavailable, null);
        }

        var truncated = folders.Count > options.MaxFolders;
        var readOnly = IsReadOnlyMount(snapshot, real!);
        var listing = new FolderListing(
            normalized,
            SafePath.SamePath(normalized, anchor!) ? null : SafePath.Parent(normalized),
            Crumbs(anchor!, normalized),
            new PathAccess(true, true, !readOnly && access.CanWrite(real!), readOnly, false),
            truncated ? folders.Take(options.MaxFolders).ToArray() : folders,
            truncated);
        return new BrowseResult(PathProblem.None, listing);
    }

    private List<FolderEntry> ListFolders(string real, string lexical, IReadOnlyList<StorageRoot> roots)
    {
        var enumeration = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        var folders = new List<FolderEntry>();
        foreach (var directory in new DirectoryInfo(real).EnumerateDirectories("*", enumeration))
        {
            // Dot folders are hidden on Unix; the attribute filter above only knows that on Unix.
            if (directory.Name.StartsWith('.'))
            {
                continue;
            }

            var isLink = directory.LinkTarget is not null;
            if (isLink)
            {
                // A link is listed only when it leads to somewhere that is itself browsable.
                var target = SafePath.ResolveReal(directory.FullName, out _);
                if (target is null || !IsUnderAnyRoot(roots, target))
                {
                    continue;
                }
            }

            folders.Add(new FolderEntry(directory.Name, Path.Combine(lexical, directory.Name), isLink));
        }

        folders.Sort((left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(left.Name, right.Name);
        });
        return folders;
    }

    private static List<FolderCrumb> Crumbs(string anchor, string normalized)
    {
        var crumbs = new List<FolderCrumb> { new(anchor, anchor) };
        var current = anchor;
        foreach (var segment in SafePath.Split(normalized[anchor.Length..]))
        {
            current = Path.Combine(current, segment);
            crumbs.Add(new FolderCrumb(segment, current));
        }

        return crumbs;
    }

    private PathCheck Check(string? path, bool directoryOnly, string? otherPath)
    {
        var snapshot = mounts.GetMounts();
        var roots = DiscoverRoots(snapshot);
        var problem = Authorize(path, roots, out var normalized, out var real, out _);
        if (problem is PathProblem.Invalid or PathProblem.NotAbsolute or PathProblem.Traversal)
        {
            return new PathCheck(null, problem, PathKind.Missing, NoAccess);
        }

        var relation = otherPath is null ? FolderRelation.Unrelated : Relate(normalized, otherPath, resolveLinks: true);
        if (problem != PathProblem.None)
        {
            return new PathCheck(normalized, problem, PathKind.Missing, NoAccess with { Unavailable = problem == PathProblem.Unavailable }, relation);
        }

        var readOnly = IsReadOnlyMount(snapshot, real!);
        var kind = KindOf(real!);
        if (kind == PathKind.Missing)
        {
            return new PathCheck(normalized, PathProblem.NotFound, kind, NoAccess with { ReadOnlyMount = readOnly }, relation);
        }

        if (kind != PathKind.Directory)
        {
            // Files are reported as present and nothing else: readability of a file is not asked here.
            return new PathCheck(
                normalized,
                directoryOnly ? PathProblem.NotADirectory : PathProblem.None,
                kind,
                new PathAccess(true, false, false, readOnly, false),
                relation);
        }

        var readable = access.CanRead(real!);
        var directoryAccess = new PathAccess(true, readable, readable && !readOnly && access.CanWrite(real!), readOnly, false);
        return new PathCheck(normalized, readable ? PathProblem.None : PathProblem.NotReadable, kind, directoryAccess, relation);
    }

    private CreateFolderResult CreateFolder(string? parent, string? name)
    {
        var nameProblem = FolderNameRules.Validate(name, out var cleaned);
        if (nameProblem != FolderNameProblem.None)
        {
            return new CreateFolderResult(CreateFolderOutcome.InvalidName, NameProblem: nameProblem);
        }

        var snapshot = mounts.GetMounts();
        var roots = DiscoverRoots(snapshot);
        var problem = Authorize(parent, roots, out var normalized, out var real, out _);
        if (problem != PathProblem.None)
        {
            return new CreateFolderResult(CreateFolderOutcome.InvalidParent, ParentProblem: problem);
        }

        var kind = KindOf(real!);
        if (kind != PathKind.Directory)
        {
            return new CreateFolderResult(
                CreateFolderOutcome.InvalidParent,
                ParentProblem: kind == PathKind.Missing ? PathProblem.NotFound : PathProblem.NotADirectory);
        }

        if (IsReadOnlyMount(snapshot, real!))
        {
            return new CreateFolderResult(CreateFolderOutcome.ReadOnly);
        }

        if (!access.CanWrite(real!))
        {
            return new CreateFolderResult(CreateFolderOutcome.NotWritable);
        }

        // The folder is created below the resolved parent, never below the path as typed.
        var target = Path.Combine(real!, cleaned);
        if (Path.Exists(target) || IsLink(target))
        {
            return new CreateFolderResult(CreateFolderOutcome.AlreadyExists);
        }

        try
        {
            Directory.CreateDirectory(target);
        }
        catch (UnauthorizedAccessException)
        {
            return new CreateFolderResult(CreateFolderOutcome.NotWritable);
        }
        catch (IOException)
        {
            return new CreateFolderResult(CreateFolderOutcome.Failed);
        }

        // Something replaced the name with a link between the check and the create: not ours.
        if (IsLink(target) || SafePath.ResolveReal(target, out _) is not { } created || !IsUnderAnyRoot(roots, created))
        {
            return new CreateFolderResult(CreateFolderOutcome.Failed);
        }

        return new CreateFolderResult(CreateFolderOutcome.Created, Path.Combine(normalized, cleaned));
    }

    private static FolderRelation Relate(string? first, string? second, bool resolveLinks)
    {
        if (SafePath.TryNormalize(first, out var left) != PathProblem.None
            || SafePath.TryNormalize(second, out var right) != PathProblem.None)
        {
            return FolderRelation.Unrelated;
        }

        var relation = RelateNormalized(left, right);
        if (relation != FolderRelation.Unrelated || !resolveLinks)
        {
            return relation;
        }

        return SafePath.ResolveReal(left, out _) is { } realLeft && SafePath.ResolveReal(right, out _) is { } realRight
            ? RelateNormalized(realLeft, realRight)
            : FolderRelation.Unrelated;
    }

    private static FolderRelation RelateNormalized(string left, string right)
    {
        if (SafePath.SamePath(left, right))
        {
            return FolderRelation.Same;
        }

        return SafePath.IsWithin(left, right) || SafePath.IsWithin(right, left)
            ? FolderRelation.Nested
            : FolderRelation.Unrelated;
    }

    /// <summary>
    /// The storage roots: every mount the admin attached to the container plus the data folder.
    /// Mount points from the mount table are already link-free, so the mounts are not touched here
    /// (a stale network mount must not stall the listing).
    /// </summary>
    private List<StorageRoot> DiscoverRoots(IReadOnlyList<MountPoint> snapshot)
    {
        var roots = new Dictionary<string, StorageRoot>(SafePath.Comparer);
        foreach (var mount in snapshot.Where(StorageMountFilter.IsStorage))
        {
            roots[mount.Path] = new StorageRoot(mount.Path, mount.Path, mount.FileSystem, mount.ReadOnly);
        }

        if (SafePath.TryNormalize(options.DataRoot, out var data) == PathProblem.None
            && !roots.ContainsKey(data)
            && SafePath.ResolveReal(data, out _) is { } dataReal)
        {
            roots[data] = new StorageRoot(data, dataReal, null, IsReadOnlyMount(snapshot, dataReal));
        }

        return roots.Values.OrderBy(root => root.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The gate every operation passes: normalizes <paramref name="input"/>, requires it to lie under
    /// a storage root as written, resolves its links and requires the result to lie under a root as
    /// well. <paramref name="anchor"/> is the outermost root that holds the path as written.
    /// </summary>
    private static PathProblem Authorize(
        string? input,
        IReadOnlyList<StorageRoot> roots,
        out string normalized,
        out string? real,
        out string? anchor)
    {
        real = null;
        anchor = null;
        var problem = SafePath.TryNormalize(input, out normalized);
        if (problem != PathProblem.None)
        {
            return problem;
        }

        foreach (var root in roots)
        {
            var candidate = SafePath.IsWithin(root.Path, normalized)
                ? root.Path
                : SafePath.IsWithin(root.RealPath, normalized) ? root.RealPath : null;
            if (candidate is not null && (anchor is null || candidate.Length < anchor.Length))
            {
                anchor = candidate;
            }
        }

        if (anchor is null)
        {
            return PathProblem.OutsideStorage;
        }

        var resolved = SafePath.ResolveReal(normalized, out problem);
        if (resolved is null)
        {
            return problem;
        }

        if (!IsUnderAnyRoot(roots, resolved))
        {
            return PathProblem.OutsideStorage;
        }

        real = resolved;
        return PathProblem.None;
    }

    private static bool IsUnderAnyRoot(IReadOnlyList<StorageRoot> roots, string real) =>
        roots.Any(root => SafePath.IsWithin(root.RealPath, real));

    private static bool IsReadOnlyMount(IReadOnlyList<MountPoint> snapshot, string real) =>
        snapshot
            .Where(mount => SafePath.IsWithin(mount.Path, real))
            .OrderByDescending(mount => mount.Path.Length)
            .FirstOrDefault()?.ReadOnly ?? false;

    private PathAccess Probe(string real, bool readOnlyMount)
    {
        if (!Directory.Exists(real))
        {
            return new PathAccess(false, false, false, readOnlyMount, false);
        }

        var readable = access.CanRead(real);
        return new PathAccess(true, readable, readable && !readOnlyMount && access.CanWrite(real), readOnlyMount, false);
    }

    private static PathKind KindOf(string real) =>
        Directory.Exists(real) ? PathKind.Directory
        : File.Exists(real) ? PathKind.File
        : Path.Exists(real) ? PathKind.Other
        : PathKind.Missing;

    private static bool IsLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static PathAccess NoAccess { get; } = new(false, false, false, false, false);

    /// <summary>
    /// Runs blocking file system work off the request thread. A mount that hangs (a stale network
    /// share) cannot be interrupted, but it must not hold the request: after the timeout the caller
    /// gets <paramref name="fallback"/>, which every operation defines as "unavailable".
    /// </summary>
    private static async Task<T> OffloadAsync<T>(
        Func<T> work,
        T fallback,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(work, cancellationToken).WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return fallback;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }
}
