using Jularr.Web.Features.Storage.FolderBrowse;

namespace Jularr.Tests;

/// <summary>
/// The container-aware folder browser (#604): what it lists, and above all what it refuses. The
/// storage roots are temp folders standing in for the container's mounts.
/// </summary>
[TestClass]
public sealed class FolderBrowseTests
{
    private static readonly string[] MountInfoLines =
    [
        "36 35 98:0 / / rw,relatime master:1 - overlay overlay rw,lowerdir=/var/lib/docker/overlay2/l/ABC",
        "41 36 0:5 / /proc rw,nosuid,nodev,noexec - proc proc rw",
        "42 36 0:6 / /dev rw,nosuid - tmpfs tmpfs rw,size=65536k,mode=755",
        "43 42 0:7 / /dev/shm rw,nosuid,nodev,noexec - tmpfs shm rw,size=65536k",
        "44 36 0:8 / /sys ro,nosuid,nodev,noexec - sysfs sysfs ro",
        "45 44 0:9 / /sys/fs/cgroup ro - cgroup2 cgroup rw",
        "46 36 0:30 / /run/secrets ro - tmpfs tmpfs rw",
        "47 36 8:1 /var/lib/docker/containers/x/hosts /etc/hosts rw,relatime - ext4 /dev/sda1 rw",
        @"50 36 8:1 /volumes/data /data rw,relatime - ext4 /dev/sda1 rw",
        "51 36 0:60 / /host-mounts/arr_bay4_media ro,relatime - cifs //nas/media ro,vers=3.0",
        @"52 36 8:1 /x /mnt/with\040space rw - ext4 /dev/sda1 rw",
        "53 36 8:17 /y /mnt/superblock-ro rw - ext4 /dev/sdb1 ro",
        "not a mountinfo line"
    ];

    [TestMethod]
    public void MountInfoIsParsedWithEscapesAndReadOnlyFlags()
    {
        var mounts = MountInfo.Parse(MountInfoLines).ToDictionary(mount => mount.Path);

        Assert.IsFalse(mounts["/data"].ReadOnly);
        Assert.AreEqual("ext4", mounts["/data"].FileSystem);
        Assert.IsTrue(mounts["/host-mounts/arr_bay4_media"].ReadOnly, "the mount options say ro");
        Assert.AreEqual("cifs", mounts["/host-mounts/arr_bay4_media"].FileSystem);
        Assert.AreEqual("//nas/media", mounts["/host-mounts/arr_bay4_media"].Source);
        Assert.IsTrue(mounts.ContainsKey("/mnt/with space"), @"\040 is a space");
        Assert.IsTrue(mounts["/mnt/superblock-ro"].ReadOnly, "the super block options say ro");
        Assert.IsFalse(mounts.ContainsKey("not a mountinfo line"));
    }

    [TestMethod]
    public void OnlyMountsAnAdminAttachedCountAsStorage()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Mount points are POSIX paths.");
        }

        var storage = MountInfo.Parse(MountInfoLines)
            .Where(StorageMountFilter.IsStorage)
            .Select(mount => mount.Path)
            .Order()
            .ToArray();

        // Not the image (overlay root), pseudo file systems, tmpfs, the container's own /etc file
        // bind or /run secrets.
        CollectionAssert.AreEqual(
            new[] { "/data", "/host-mounts/arr_bay4_media", "/mnt/superblock-ro", "/mnt/with space" },
            storage);
    }

    [TestMethod]
    public async Task RootsAreTheMountsAndTheDataFolderWithTheirAccess()
    {
        using var storage = new Storage();
        storage.AddMount(storage.Nas, readOnly: true);
        storage.Mounts.Add(new MountPoint(Path.Combine(storage.Base, "gone"), "ext4", null, false));
        Directory.CreateDirectory(Path.Combine(storage.Base, "ram"));
        storage.Mounts.Add(new MountPoint(Path.Combine(storage.Base, "ram"), "tmpfs", null, false));
        storage.Access.Unwritable.Add(storage.Media);

        var roots = (await storage.Service.GetRootsAsync(default)).ToDictionary(root => root.Path);

        CollectionAssert.AreEquivalent(
            new[] { storage.Media, storage.Nas, storage.Data },
            roots.Keys.ToArray(),
            "A mount that is not there and a tmpfs are no locations to pick.");
        Assert.IsTrue(roots[storage.Data].Access.Writable);
        Assert.IsTrue(roots[storage.Media].Access.Readable);
        Assert.IsFalse(roots[storage.Media].Access.Writable);
        Assert.IsTrue(roots[storage.Nas].Access.ReadOnlyMount);
        Assert.IsFalse(roots[storage.Nas].Access.Writable, "a read-only mount is never writable");
        Assert.AreEqual("ext4", roots[storage.Media].FileSystem);
    }

    [TestMethod]
    public async Task ARootThatDoesNotAnswerIsListedAsUnavailable()
    {
        using var storage = new Storage(probeTimeout: TimeSpan.FromMilliseconds(150));
        storage.Access.Hang.Add(storage.Media);

        var roots = (await storage.Service.GetRootsAsync(default)).ToDictionary(root => root.Path);

        Assert.IsTrue(roots[storage.Media].Access.Unavailable);
        Assert.IsFalse(roots[storage.Data].Access.Unavailable);
    }

    [TestMethod]
    public async Task ListsSubfoldersOnlyNeverFilesOrDotFolders()
    {
        using var storage = new Storage();
        Directory.CreateDirectory(Path.Combine(storage.Media, "beta"));
        Directory.CreateDirectory(Path.Combine(storage.Media, "Alpha", "inner"));
        Directory.CreateDirectory(Path.Combine(storage.Media, ".trash"));
        File.WriteAllText(Path.Combine(storage.Media, "notes.txt"), "not for the browser");

        var result = await storage.Service.BrowseAsync(storage.Media, default);

        Assert.IsTrue(result.Succeeded);
        var listing = result.Listing!;
        CollectionAssert.AreEqual(new[] { "Alpha", "beta" }, listing.Folders.Select(folder => folder.Name).ToArray());
        Assert.AreEqual(Path.Combine(storage.Media, "Alpha"), listing.Folders[0].Path);
        Assert.IsFalse(listing.Truncated);
        Assert.IsTrue(listing.Access.Readable);
        Assert.IsTrue(listing.Access.Writable);
    }

    [TestMethod]
    public async Task BreadcrumbsAndParentFollowTheRoot()
    {
        using var storage = new Storage();
        var season = Directory.CreateDirectory(Path.Combine(storage.Media, "tv", "season1")).FullName;

        var deep = (await storage.Service.BrowseAsync(season + Path.DirectorySeparatorChar, default)).Listing!;
        var middle = (await storage.Service.BrowseAsync(Path.Combine(storage.Media, "tv"), default)).Listing!;
        var top = (await storage.Service.BrowseAsync(storage.Media, default)).Listing!;

        CollectionAssert.AreEqual(
            new[] { storage.Media, "tv", "season1" },
            deep.Crumbs.Select(crumb => crumb.Name).ToArray());
        Assert.AreEqual(season, deep.Crumbs[^1].Path);
        Assert.AreEqual(Path.Combine(storage.Media, "tv"), deep.Parent);
        Assert.AreEqual(storage.Media, middle.Parent);
        Assert.IsNull(top.Parent, "Up from the top of a root leads to the list of roots.");
    }

    [TestMethod]
    public async Task AListingIsCappedAndSaysSo()
    {
        using var storage = new Storage(maxFolders: 3);
        foreach (var name in new[] { "e", "d", "c", "b", "a" })
        {
            Directory.CreateDirectory(Path.Combine(storage.Media, name));
        }

        var listing = (await storage.Service.BrowseAsync(storage.Media, default)).Listing!;

        Assert.IsTrue(listing.Truncated);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, listing.Folders.Select(folder => folder.Name).ToArray());
    }

    [TestMethod]
    public async Task AFolderTheRuntimeUserCannotReadIsRefusedAndOneThatHangsIsUnavailable()
    {
        using var storage = new Storage(timeout: TimeSpan.FromMilliseconds(750));
        var closed = Directory.CreateDirectory(Path.Combine(storage.Media, "closed")).FullName;
        var stale = Directory.CreateDirectory(Path.Combine(storage.Media, "stale")).FullName;
        storage.Access.Unreadable.Add(closed);
        storage.Access.Hang.Add(stale);

        Assert.AreEqual(PathProblem.NotReadable, (await storage.Service.BrowseAsync(closed, default)).Problem);
        Assert.AreEqual(PathProblem.Unavailable, (await storage.Service.BrowseAsync(stale, default)).Problem);
    }

    [TestMethod]
    public async Task MissingPathsAndFilesAreNotBrowsable()
    {
        using var storage = new Storage();
        var file = Path.Combine(storage.Media, "movie.mkv");
        File.WriteAllText(file, "x");

        Assert.AreEqual(PathProblem.NotFound, (await storage.Service.BrowseAsync(Path.Combine(storage.Media, "nope"), default)).Problem);
        Assert.AreEqual(PathProblem.NotADirectory, (await storage.Service.BrowseAsync(file, default)).Problem);
    }

    [TestMethod]
    [DataRow("{media}/../secret", PathProblem.Traversal)]
    [DataRow("{media}/sub/../../secret", PathProblem.Traversal)]
    [DataRow("{media}/..", PathProblem.Traversal)]
    [DataRow("relative/folder", PathProblem.NotAbsolute)]
    [DataRow("./folder", PathProblem.NotAbsolute)]
    [DataRow("", PathProblem.Invalid)]
    [DataRow("   ", PathProblem.Invalid)]
    [DataRow("{media}/a\0b", PathProblem.Invalid)]
    [DataRow("{media}/a\nb", PathProblem.Invalid)]
    public async Task TraversalAndMalformedPathsAreRefusedEverywhere(string template, PathProblem expected)
    {
        using var storage = new Storage();
        Directory.CreateDirectory(Path.Combine(storage.Base, "secret"));
        var path = template.Replace("{media}", storage.Media);

        Assert.AreEqual(expected, (await storage.Service.BrowseAsync(path, default)).Problem, "browse");
        Assert.AreEqual(expected, (await storage.Service.CheckAsync(path, true, null, default)).Problem, "check");
        var created = await storage.Service.CreateFolderAsync(path, "new", default);
        Assert.AreEqual(CreateFolderOutcome.InvalidParent, created.Outcome, "create");
        Assert.AreEqual(expected, created.ParentProblem, "create");
        Assert.IsFalse(Directory.Exists(Path.Combine(storage.Base, "secret", "new")));
    }

    [TestMethod]
    public async Task NothingOutsideTheRootsIsBrowsable()
    {
        using var storage = new Storage();
        var lookalike = Directory.CreateDirectory(storage.Media + "-other").FullName;

        foreach (var path in new[]
                 {
                     storage.Outside,
                     Path.GetDirectoryName(storage.Media)!,
                     lookalike,
                     Path.GetPathRoot(storage.Media)!
                 })
        {
            Assert.AreEqual(PathProblem.OutsideStorage, (await storage.Service.BrowseAsync(path, default)).Problem, path);
            Assert.AreEqual(PathProblem.OutsideStorage, (await storage.Service.CheckAsync(path, true, null, default)).Problem, path);
            Assert.AreEqual(
                CreateFolderOutcome.InvalidParent,
                (await storage.Service.CreateFolderAsync(path, "new", default)).Outcome,
                path);
        }

        Assert.IsFalse(Directory.Exists(Path.Combine(storage.Outside, "new")));
    }

    [TestMethod]
    public async Task ALinkOutOfTheStorageIsNeitherFollowedNorListed()
    {
        using var storage = new Storage();
        var secret = Directory.CreateDirectory(Path.Combine(storage.Outside, "secret")).FullName;
        Link(Path.Combine(storage.Media, "escape"), storage.Outside);
        Directory.CreateDirectory(Path.Combine(storage.Media, "fine"));

        var listing = (await storage.Service.BrowseAsync(storage.Media, default)).Listing!;
        var through = Path.Combine(storage.Media, "escape", "secret");

        CollectionAssert.AreEqual(new[] { "fine" }, listing.Folders.Select(folder => folder.Name).ToArray());
        Assert.AreEqual(PathProblem.OutsideStorage, (await storage.Service.BrowseAsync(Path.Combine(storage.Media, "escape"), default)).Problem);
        Assert.AreEqual(PathProblem.OutsideStorage, (await storage.Service.BrowseAsync(through, default)).Problem);
        Assert.AreEqual(PathProblem.OutsideStorage, (await storage.Service.CheckAsync(through, true, null, default)).Problem);
        Assert.IsTrue(Directory.Exists(secret), "the target itself is untouched");
    }

    [TestMethod]
    public async Task ARelativeLinkThatClimbsOutIsRefused()
    {
        using var storage = new Storage();
        var sub = Directory.CreateDirectory(Path.Combine(storage.Media, "sub")).FullName;
        Link(Path.Combine(sub, "up"), Path.Combine("..", "..", Path.GetFileName(storage.Outside)));

        Assert.AreEqual(PathProblem.OutsideStorage, (await storage.Service.BrowseAsync(Path.Combine(sub, "up"), default)).Problem);
        Assert.AreEqual(0, (await storage.Service.BrowseAsync(sub, default)).Listing!.Folders.Count);
    }

    [TestMethod]
    public async Task AChainOfLinksIsResolvedToItsEnd()
    {
        using var storage = new Storage();
        Link(Path.Combine(storage.Media, "second"), storage.Outside);
        Link(Path.Combine(storage.Media, "first"), Path.Combine(storage.Media, "second"));

        Assert.AreEqual(PathProblem.OutsideStorage, (await storage.Service.BrowseAsync(Path.Combine(storage.Media, "first"), default)).Problem);
        Assert.AreEqual(0, (await storage.Service.BrowseAsync(storage.Media, default)).Listing!.Folders.Count);
    }

    [TestMethod]
    public async Task LinksThatLoopAreUnavailableAndNotListed()
    {
        using var storage = new Storage();
        Link(Path.Combine(storage.Media, "a"), Path.Combine(storage.Media, "b"));
        Link(Path.Combine(storage.Media, "b"), Path.Combine(storage.Media, "a"));

        Assert.AreEqual(PathProblem.Unavailable, (await storage.Service.BrowseAsync(Path.Combine(storage.Media, "a"), default)).Problem);
        Assert.AreEqual(0, (await storage.Service.BrowseAsync(storage.Media, default)).Listing!.Folders.Count);
    }

    [TestMethod]
    public async Task ALinkToAnotherFolderOfTheStorageIsFollowedAndMarked()
    {
        using var storage = new Storage();
        var real = Directory.CreateDirectory(Path.Combine(storage.Media, "real", "child")).FullName;
        Link(Path.Combine(storage.Media, "shortcut"), Path.Combine(storage.Media, "real"));

        var listing = (await storage.Service.BrowseAsync(storage.Media, default)).Listing!;
        var viaLink = await storage.Service.BrowseAsync(Path.Combine(storage.Media, "shortcut"), default);

        Assert.IsTrue(listing.Folders.Single(folder => folder.Name == "shortcut").IsLink);
        Assert.IsFalse(listing.Folders.Single(folder => folder.Name == "real").IsLink);
        Assert.IsTrue(viaLink.Succeeded);
        Assert.AreEqual("child", viaLink.Listing!.Folders.Single().Name);
        Assert.AreEqual(Path.Combine(storage.Media, "shortcut", "child"), viaLink.Listing.Folders.Single().Path, "paths stay as navigated");
        Assert.IsTrue(Directory.Exists(real));
    }

    [TestMethod]
    public async Task ALinkFromOneRootIntoAnotherIsAllowed()
    {
        using var storage = new Storage();
        storage.AddMount(storage.Nas, readOnly: false);
        Directory.CreateDirectory(Path.Combine(storage.Nas, "shows"));
        Link(Path.Combine(storage.Media, "nas"), storage.Nas);

        var result = await storage.Service.BrowseAsync(Path.Combine(storage.Media, "nas"), default);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("shows", result.Listing!.Folders.Single().Name);
    }

    [TestMethod]
    public async Task ALinkDoesNotLetAFolderBeCreatedOutsideTheStorage()
    {
        using var storage = new Storage();
        Link(Path.Combine(storage.Media, "escape"), storage.Outside);

        var result = await storage.Service.CreateFolderAsync(Path.Combine(storage.Media, "escape"), "planted", default);

        Assert.AreEqual(CreateFolderOutcome.InvalidParent, result.Outcome);
        Assert.AreEqual(PathProblem.OutsideStorage, result.ParentProblem);
        Assert.IsFalse(Directory.Exists(Path.Combine(storage.Outside, "planted")));
    }

    [TestMethod]
    public async Task ALinkAlreadyNamedLikeTheNewFolderIsNotReplacedOrFollowed()
    {
        using var storage = new Storage();
        Link(Path.Combine(storage.Media, "taken"), storage.Outside);

        var result = await storage.Service.CreateFolderAsync(storage.Media, "taken", default);

        Assert.AreEqual(CreateFolderOutcome.AlreadyExists, result.Outcome);
    }

    [TestMethod]
    public async Task CreatesAFolderInsideAWritableLocation()
    {
        using var storage = new Storage();
        var parent = Directory.CreateDirectory(Path.Combine(storage.Media, "manga")).FullName;

        var result = await storage.Service.CreateFolderAsync(parent + Path.DirectorySeparatorChar, "  One Piece  ", default);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(Path.Combine(parent, "One Piece"), result.Path, "the name is trimmed, nothing else is rewritten");
        Assert.IsTrue(Directory.Exists(Path.Combine(parent, "One Piece")));
        Assert.AreEqual(
            CreateFolderOutcome.AlreadyExists,
            (await storage.Service.CreateFolderAsync(parent, "One Piece", default)).Outcome);
    }

    [TestMethod]
    [DataRow("", FolderNameProblem.Empty)]
    [DataRow("   ", FolderNameProblem.Empty)]
    [DataRow(".", FolderNameProblem.Reserved)]
    [DataRow("..", FolderNameProblem.Reserved)]
    [DataRow("../evil", FolderNameProblem.InvalidCharacter)]
    [DataRow("a/b", FolderNameProblem.InvalidCharacter)]
    [DataRow("a\\b", FolderNameProblem.InvalidCharacter)]
    [DataRow("what?", FolderNameProblem.InvalidCharacter)]
    [DataRow("c:evil", FolderNameProblem.InvalidCharacter)]
    [DataRow("tab\there", FolderNameProblem.InvalidCharacter)]
    [DataRow("nul\0byte", FolderNameProblem.InvalidCharacter)]
    [DataRow("gnp‮gpj", FolderNameProblem.InvalidCharacter)]
    [DataRow("ends-with-dot.", FolderNameProblem.EdgeCharacter)]
    [DataRow("...", FolderNameProblem.EdgeCharacter)]
    public async Task UnsafeFolderNamesAreRefusedAndNothingIsCreated(string name, FolderNameProblem expected)
    {
        using var storage = new Storage();
        var before = Directory.GetFileSystemEntries(storage.Base, "*", SearchOption.AllDirectories).Length;

        var result = await storage.Service.CreateFolderAsync(storage.Media, name, default);

        Assert.AreEqual(CreateFolderOutcome.InvalidName, result.Outcome);
        Assert.AreEqual(expected, result.NameProblem);
        Assert.AreEqual(before, Directory.GetFileSystemEntries(storage.Base, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task ANameOver255BytesIsRefused()
    {
        using var storage = new Storage();

        Assert.AreEqual(
            FolderNameProblem.None,
            (await storage.Service.CreateFolderAsync(storage.Media, new string('a', 255), default)).NameProblem);
        Assert.AreEqual(
            FolderNameProblem.TooLong,
            (await storage.Service.CreateFolderAsync(storage.Media, new string('a', 256), default)).NameProblem);
        // 86 three-byte characters are 258 bytes: the limit is bytes, not characters.
        Assert.AreEqual(
            FolderNameProblem.TooLong,
            (await storage.Service.CreateFolderAsync(storage.Media, new string('あ', 86), default)).NameProblem);
    }

    [TestMethod]
    public async Task FoldersAreOnlyCreatedWhereJularrMayWrite()
    {
        using var storage = new Storage();
        storage.AddMount(storage.Nas, readOnly: true);
        var readOnlyChild = Directory.CreateDirectory(Path.Combine(storage.Nas, "child")).FullName;
        var locked = Directory.CreateDirectory(Path.Combine(storage.Media, "locked")).FullName;
        storage.Access.Unwritable.Add(locked);

        Assert.AreEqual(CreateFolderOutcome.ReadOnly, (await storage.Service.CreateFolderAsync(readOnlyChild, "new", default)).Outcome);
        Assert.AreEqual(CreateFolderOutcome.NotWritable, (await storage.Service.CreateFolderAsync(locked, "new", default)).Outcome);
        Assert.AreEqual(
            CreateFolderOutcome.InvalidParent,
            (await storage.Service.CreateFolderAsync(Path.Combine(storage.Media, "missing"), "new", default)).Outcome);
        Assert.IsFalse(Directory.Exists(Path.Combine(readOnlyChild, "new")));
        Assert.IsFalse(Directory.Exists(Path.Combine(locked, "new")));
    }

    [TestMethod]
    public async Task CheckReportsExistenceReadabilityAndWritability()
    {
        using var storage = new Storage();
        var folder = Directory.CreateDirectory(Path.Combine(storage.Media, "lib")).FullName;
        var file = Path.Combine(storage.Media, "movie.mkv");
        File.WriteAllText(file, "x");
        storage.Access.Unwritable.Add(folder);

        var present = await storage.Service.CheckAsync(folder, true, null, default);
        var missing = await storage.Service.CheckAsync(Path.Combine(storage.Media, "nope", "deeper"), true, null, default);
        var asFolder = await storage.Service.CheckAsync(file, true, null, default);
        var asAny = await storage.Service.CheckAsync(file, false, null, default);

        Assert.AreEqual(PathProblem.None, present.Problem);
        Assert.AreEqual(PathKind.Directory, present.Kind);
        Assert.IsTrue(present.Access is { Exists: true, Readable: true, Writable: false });
        Assert.AreEqual(PathProblem.NotFound, missing.Problem);
        Assert.IsFalse(missing.Exists);
        Assert.AreEqual(PathProblem.NotADirectory, asFolder.Problem);
        Assert.AreEqual(PathProblem.None, asAny.Problem);
        Assert.AreEqual(PathKind.File, asAny.Kind);
    }

    [TestMethod]
    public async Task CheckSeesWhenTwoFoldersAreTheSameOrNested()
    {
        using var storage = new Storage();
        var library = Directory.CreateDirectory(Path.Combine(storage.Media, "library")).FullName;
        var inbox = Directory.CreateDirectory(Path.Combine(storage.Media, "inbox")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(library, "inbox")).FullName;

        Assert.AreEqual(FolderRelation.Unrelated, (await storage.Service.CheckAsync(inbox, true, library, default)).Relation);
        Assert.AreEqual(FolderRelation.Same, (await storage.Service.CheckAsync(library, true, library + Path.DirectorySeparatorChar, default)).Relation);
        Assert.AreEqual(FolderRelation.Nested, (await storage.Service.CheckAsync(nested, true, library, default)).Relation);
        Assert.AreEqual(FolderRelation.Nested, (await storage.Service.CheckAsync(library, true, nested, default)).Relation);
        Assert.AreEqual(
            FolderRelation.Unrelated,
            (await storage.Service.CheckAsync(library, true, library + "-2", default)).Relation,
            "A shared name prefix is not nesting.");
    }

    [TestMethod]
    public async Task ComparePairResolvesLinksAndIgnoresPathsThatAreNoPaths()
    {
        using var storage = new Storage();
        var library = Directory.CreateDirectory(Path.Combine(storage.Media, "library")).FullName;
        Link(Path.Combine(storage.Media, "alias"), library);

        Assert.AreEqual(FolderRelation.Same, await storage.Service.ComparePairAsync(library, Path.Combine(storage.Media, "alias"), default));
        Assert.AreEqual(FolderRelation.Unrelated, await storage.Service.ComparePairAsync(library, "not/absolute", default));
        Assert.AreEqual(FolderRelation.Unrelated, await storage.Service.ComparePairAsync(library, null, default));
    }

    [TestMethod]
    public void TheRealFileSystemAnswersForTheRuntimeUser()
    {
        var access = new DirectoryAccess();
        var folder = Directory.CreateTempSubdirectory("jularr-access-");
        try
        {
            Assert.IsTrue(access.CanRead(folder.FullName));
            Assert.IsTrue(access.CanWrite(folder.FullName));
            Assert.IsFalse(access.CanRead(Path.Combine(folder.FullName, "missing")));
            Assert.IsFalse(access.CanWrite(Path.Combine(folder.FullName, "missing")));
            Assert.AreEqual(0, folder.GetFileSystemInfos().Length, "checking access leaves nothing behind");

            if (!OperatingSystem.IsWindows() && Environment.UserName != "root")
            {
                File.SetUnixFileMode(folder.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                Assert.IsTrue(access.CanRead(folder.FullName));
                Assert.IsFalse(access.CanWrite(folder.FullName), "a folder without the write bit is not writable");
                File.SetUnixFileMode(folder.FullName, UnixFileMode.None);
                Assert.IsFalse(access.CanRead(folder.FullName));
            }
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(folder.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void TheSystemMountTableReadsTheMountsOfTheRunningProcess()
    {
        var mounts = new SystemMountTable().GetMounts();

        Assert.IsTrue(mounts.Count > 0);
        Assert.IsTrue(mounts.All(mount => Path.IsPathFullyQualified(mount.Path)));
    }

    private static void Link(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            Assert.Inconclusive($"This user may not create symbolic links here: {exception.Message}");
        }
    }

    private sealed class Storage : IDisposable
    {
        public Storage(TimeSpan? timeout = null, TimeSpan? probeTimeout = null, int maxFolders = 1000)
        {
            Base = Path.Combine(Path.GetTempPath(), $"jularr-folders-{Guid.NewGuid():N}");
            Media = Directory.CreateDirectory(Path.Combine(Base, "media")).FullName;
            Nas = Directory.CreateDirectory(Path.Combine(Base, "nas")).FullName;
            Data = Directory.CreateDirectory(Path.Combine(Base, "data")).FullName;
            Outside = Directory.CreateDirectory(Path.Combine(Base, "outside")).FullName;
            Mounts = new FakeMountTable([new MountPoint(Media, "ext4", "/dev/sda1", ReadOnly: false)]);
            Access = new FakeAccess();
            Service = new FolderBrowseService(
                Mounts,
                Access,
                new FolderBrowseOptions(Data)
                {
                    Timeout = timeout ?? TimeSpan.FromSeconds(10),
                    ProbeTimeout = probeTimeout ?? TimeSpan.FromSeconds(10),
                    MaxFolders = maxFolders
                });
        }

        public string Base { get; }

        /// <summary>A mounted volume.</summary>
        public string Media { get; }

        /// <summary>A second volume, added with <see cref="AddMount"/>.</summary>
        public string Nas { get; }

        /// <summary>Jularr's data folder, browsable although it is no mount here.</summary>
        public string Data { get; }

        /// <summary>A folder of the same machine that is not mounted into the "container".</summary>
        public string Outside { get; }

        public FakeMountTable Mounts { get; }

        public FakeAccess Access { get; }

        public FolderBrowseService Service { get; }

        public void AddMount(string path, bool readOnly) =>
            Mounts.Add(new MountPoint(path, "cifs", "//nas/share", readOnly));

        public void Dispose()
        {
            try
            {
                Directory.Delete(Base, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    internal sealed class FakeMountTable(IEnumerable<MountPoint> mounts) : IMountTable
    {
        private readonly List<MountPoint> entries = [.. mounts];

        public void Add(MountPoint mount) => entries.Add(mount);

        public IReadOnlyList<MountPoint> GetMounts() => entries.ToArray();
    }

    /// <summary>Stands in for the runtime user's permissions, which a test cannot rely on.</summary>
    internal sealed class FakeAccess : IDirectoryAccess
    {
        public HashSet<string> Unreadable { get; } = [];

        public HashSet<string> Unwritable { get; } = [];

        /// <summary>Folders whose probe outlasts any timeout, like a stale network share.</summary>
        public HashSet<string> Hang { get; } = [];

        public bool CanRead(string directory)
        {
            Stall(directory);
            return !Unreadable.Contains(directory);
        }

        public bool CanWrite(string directory)
        {
            Stall(directory);
            return !Unwritable.Contains(directory);
        }

        private void Stall(string directory)
        {
            if (Hang.Contains(directory))
            {
                Thread.Sleep(TimeSpan.FromSeconds(2));
            }
        }
    }
}
