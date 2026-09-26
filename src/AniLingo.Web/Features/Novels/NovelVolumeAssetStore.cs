using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Novels;

/// <summary>
/// Cache of normalized EPUB volume assets (cover and inline illustrations)
/// under AniLingo data. Source EPUB files are never written; assets are
/// content-addressed per volume so re-importing a volume reuses unchanged
/// files, and anything no longer referenced is pruned.
/// </summary>
public sealed partial class NovelVolumeAssetStore
{
    public const string RootPath = "/data/novels/volumes";

    private readonly DirectoryInfo root;

    public NovelVolumeAssetStore()
        : this(new DirectoryInfo(RootPath))
    {
    }

    public NovelVolumeAssetStore(DirectoryInfo root)
    {
        ArgumentNullException.ThrowIfNull(root);
        this.root = root;
    }

    public static string Url(Guid volumeId, string asset) =>
        $"/Novels/Asset/{volumeId:N}/{asset}";

    /// <summary>Stores an image and returns its asset name.</summary>
    public async Task<string?> SaveAsync(
        Guid volumeId,
        byte[] bytes,
        string mediaType,
        CancellationToken cancellationToken)
    {
        var extension = ExtensionFor(mediaType);
        if (extension is null || bytes.Length == 0)
        {
            return null;
        }

        var name = Convert.ToHexString(SHA256.HashData(bytes))[..32].ToLowerInvariant()
            + extension;
        var directory = VolumeDirectory(volumeId);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }

        return name;
    }

    /// <summary>Deletes cached files of a volume that are no longer referenced.</summary>
    public void Prune(Guid volumeId, IReadOnlySet<string> keep)
    {
        var directory = VolumeDirectory(volumeId);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (keep.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public void DeleteVolume(Guid volumeId)
    {
        var directory = VolumeDirectory(volumeId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Resolves an asset path for serving. Only content-addressed raster file
    /// names are accepted, so a request can never leave the volume directory.
    /// </summary>
    public string? Resolve(Guid volumeId, string? asset)
    {
        if (string.IsNullOrEmpty(asset) || !AssetName().IsMatch(asset))
        {
            return null;
        }

        var path = Path.Combine(VolumeDirectory(volumeId), asset);
        return File.Exists(path) ? path : null;
    }

    public static bool IsAssetName(string? asset) =>
        !string.IsNullOrEmpty(asset) && AssetName().IsMatch(asset);

    public static string ContentType(string asset) =>
        Path.GetExtension(asset).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

    private string VolumeDirectory(Guid volumeId) =>
        Path.Combine(root.FullName, volumeId.ToString("N"));

    private static string? ExtensionFor(string mediaType) =>
        mediaType.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => null
        };

    [GeneratedRegex(@"^[a-f0-9]{32}\.(jpg|png|gif|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex AssetName();
}
