namespace AniLingo.Web.Features.Library;

// Built from the scan's single recursive root enumeration, so NFO discovery lists no directory again.
// Lookups ignore case; when names differ only by case the ordinal-first path wins.
public sealed class NfoFileIndex
{
    public const string ShowFileName = "tvshow.nfo";

    private const string Extension = ".nfo";

    private readonly Dictionary<string, string> paths = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsNfo(string path) =>
        Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);

    public void Add(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!paths.TryGetValue(fullPath, out var existing) ||
            string.CompareOrdinal(fullPath, existing) < 0)
        {
            paths[fullPath] = fullPath;
        }
    }

    public string? FindShow(string seriesDirectory) =>
        Find(Path.Combine(Path.GetFullPath(seriesDirectory), ShowFileName));

    public string? FindEpisode(string mediaPath)
    {
        var fullPath = Path.GetFullPath(mediaPath);
        return Find(Path.Combine(
            Path.GetDirectoryName(fullPath)!,
            Path.GetFileNameWithoutExtension(fullPath) + Extension));
    }

    private string? Find(string path) =>
        paths.GetValueOrDefault(path);
}
