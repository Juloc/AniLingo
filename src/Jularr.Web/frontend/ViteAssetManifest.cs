using System.Text.Json;

namespace Jularr.Web.Frontend;

/// <summary>
/// Resolves versioned frontend entries emitted by Vite. Missing build output is tolerated so a
/// backend-only development run remains usable while the frontend migration is incremental.
/// </summary>
public sealed class ViteAssetManifest(IWebHostEnvironment environment)
{
    private const string AssetRoot = "/build/";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly string manifestPath = Path.Combine(environment.WebRootPath, "build", ".vite", "manifest.json");
    private readonly object sync = new();
    private DateTime manifestWriteTimeUtc = DateTime.MinValue;
    private IReadOnlyDictionary<string, ManifestEntry> entries = new Dictionary<string, ManifestEntry>();

    public ViteAssetBundle Resolve(string entry)
    {
        var currentEntries = LoadEntries();
        if (!currentEntries.TryGetValue(entry, out var root))
        {
            return ViteAssetBundle.Empty;
        }

        var stylesheets = new List<string>();
        var preloadScripts = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string key, bool preload)
        {
            if (!visited.Add(key) || !currentEntries.TryGetValue(key, out var current))
            {
                return;
            }

            if (current.Css is not null)
            {
                stylesheets.AddRange(current.Css.Select(ToUrl));
            }

            if (preload)
            {
                preloadScripts.Add(ToUrl(current.File));
            }

            if (current.Imports is not null)
            {
                foreach (var import in current.Imports)
                {
                    Visit(import, preload: true);
                }
            }
        }

        Visit(entry, preload: false);
        return new ViteAssetBundle(ToUrl(root.File), stylesheets.Distinct(StringComparer.Ordinal).ToArray(), preloadScripts.Distinct(StringComparer.Ordinal).ToArray());
    }

    private IReadOnlyDictionary<string, ManifestEntry> LoadEntries()
    {
        var manifestInfo = new FileInfo(manifestPath);
        if (!manifestInfo.Exists)
        {
            return [];
        }

        lock (sync)
        {
            if (manifestInfo.LastWriteTimeUtc == manifestWriteTimeUtc)
            {
                return entries;
            }

            using var stream = manifestInfo.OpenRead();
            entries = JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(stream, JsonOptions)
                ?? new Dictionary<string, ManifestEntry>();
            manifestWriteTimeUtc = manifestInfo.LastWriteTimeUtc;
            return entries;
        }
    }

    private static string ToUrl(string assetPath) => AssetRoot + assetPath.Replace('\\', '/');

    private sealed record ManifestEntry(string File, string[]? Css, string[]? Imports);
}

public sealed record ViteAssetBundle(string? EntryScript, IReadOnlyList<string> Stylesheets, IReadOnlyList<string> PreloadScripts)
{
    public static ViteAssetBundle Empty { get; } = new(null, [], []);
}
