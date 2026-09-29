using Jularr.Web.Data;

namespace Jularr.Web.Features.Artwork;

/// <summary>What happened when a caller asked to persist artwork beside the media.</summary>
public enum ArtworkPersistOutcome
{
    Saved,
    Current,
    KeptCustom,
    Unavailable,
    Rejected,
    Failed
}

/// <summary>
/// The one write/read path for durable artwork kept beside a work's media on its configured NAS
/// library root (issue #406), shared by every media type that is not Anime -- Books covers and,
/// through <see cref="ReadingCoverArtwork"/> (issue #581), Light Novel and Manga series covers. It
/// records ownership through the same <see cref="MediaArtworkAssetStore"/> rows and atomic
/// write-then-verify discipline as <see cref="AnimeArtworkLibrary"/> (which keeps its own richer
/// per-season precedence chain), so a file without a row -- or one that changed size/timestamp
/// since Jularr wrote it -- is the user's own and is never replaced. The local derivative cache
/// (<see cref="BesideMediaArtworkCache"/>, #570) sits in front of <see cref="ResolveAsync"/>
/// without a second artwork write path.
/// </summary>
public sealed class BesideMediaArtworkStore(AppDbContext db)
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly MediaArtworkAssetStore assets = new(db);

    /// <summary>The file to show for one owner/kind in its media folder, or null.</summary>
    public async Task<string?> ResolveAsync(
        string scope,
        Guid ownerId,
        string kind,
        string folder,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var record = await FindAsync(scope, ownerId, kind, cancellationToken);
        var managedName = record is not null && IsIntact(folder, record) ? record.FileName : null;
        var candidates = FindFiles(folder, kind);
        return candidates.FirstOrDefault(path =>
                   managedName is null ||
                   !string.Equals(Path.GetFileName(path), managedName, StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault();
    }

    /// <summary>Writes artwork beside the media unless the user's own file, or already-current
    /// Jularr artwork, is there. Never overwrites a file it did not write itself.</summary>
    public async Task<ArtworkPersistOutcome> PersistAsync(
        string scope,
        Guid ownerId,
        string kind,
        string folder,
        byte[] bytes,
        string source,
        string? identity,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            return ArtworkPersistOutcome.Unavailable;
        }

        var extension = bytes.Length > AnimeArtworkFiles.MaxImageBytes ? null : AnimeArtworkFiles.DetectExtension(bytes);
        if (extension is null)
        {
            return ArtworkPersistOutcome.Rejected;
        }

        var record = await FindAsync(scope, ownerId, kind, cancellationToken);
        var managed = record is not null && IsIntact(folder, record) ? record : null;
        var custom = FindFiles(folder, kind).Any(file =>
            managed is null || !string.Equals(Path.GetFileName(file), managed.FileName, StringComparison.OrdinalIgnoreCase));
        if (custom)
        {
            return ArtworkPersistOutcome.KeptCustom;
        }

        if (managed is not null &&
            string.Equals(source, managed.Source, StringComparison.Ordinal) &&
            (identity is null || string.Equals(identity, managed.SourceIdentity, StringComparison.Ordinal)))
        {
            return ArtworkPersistOutcome.Current;
        }

        var fileName = kind + extension;
        var path = Path.Combine(folder, fileName);
        try
        {
            await AnimeArtworkFiles.WriteAtomicAsync(path, bytes, cancellationToken);
            if (!await AnimeArtworkFiles.ContentEqualsAsync(path, bytes, cancellationToken))
            {
                AnimeArtworkFiles.TryDelete(path);
                return ArtworkPersistOutcome.Failed;
            }

            // Replacing cover.jpg with a PNG must not leave the old cover.jpg behind.
            if (managed is not null && !string.Equals(managed.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                AnimeArtworkFiles.TryDelete(Path.Combine(folder, managed.FileName));
            }

            var file = new FileInfo(path);
            await assets.UpsertAsync(
                new MediaArtworkAsset(scope, ownerId, 0, kind, file.Name, source, identity, file.Length, file.LastWriteTimeUtc),
                cancellationToken);
            return ArtworkPersistOutcome.Saved;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ArtworkPersistOutcome.Failed;
        }
    }

    private async Task<MediaArtworkAsset?> FindAsync(
        string scope, Guid ownerId, string kind, CancellationToken cancellationToken)
    {
        var rows = await assets.ListAsync(scope, ownerId, cancellationToken);
        return rows.FirstOrDefault(row => string.Equals(row.Kind, kind, StringComparison.Ordinal));
    }

    private static bool IsIntact(string folder, MediaArtworkAsset record) =>
        record.Matches(new FileInfo(Path.Combine(folder, record.FileName)));

    private static IReadOnlyList<string> FindFiles(string folder, string kind)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        // Ordinal order keeps the choice deterministic when a case-sensitive filesystem holds
        // both "Cover.jpg" and "cover.jpg".
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory
                     .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            files.TryAdd(Path.GetFileName(path), path);
        }

        var result = new List<string>();
        foreach (var extension in Extensions)
        {
            if (files.TryGetValue(kind + extension, out var candidate))
            {
                result.Add(candidate);
            }
        }

        return result;
    }
}
