namespace AniLingo.Web.Features.Library;

// Reuses directory listings for one reconciliation pass so a season folder is listed once,
// not once per episode. Failed listings are not cached and keep throwing to the caller.
public sealed class SubtitleSidecarDirectoryCache
{
    private readonly Dictionary<string, string[]> files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> directories = new(StringComparer.Ordinal);

    public int Listings { get; private set; }

    public IReadOnlyList<string> GetFiles(string directory) =>
        GetOrList(files, directory, Directory.EnumerateFiles);

    public IReadOnlyList<string> GetDirectories(string directory) =>
        GetOrList(directories, directory, Directory.EnumerateDirectories);

    private string[] GetOrList(
        Dictionary<string, string[]> cache,
        string directory,
        Func<string, IEnumerable<string>> list)
    {
        var key = Path.GetFullPath(directory);
        if (!cache.TryGetValue(key, out var entries))
        {
            entries = list(key).ToArray();
            Listings++;
            cache.Add(key, entries);
        }

        return entries;
    }
}
