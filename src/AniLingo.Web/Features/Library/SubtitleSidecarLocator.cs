namespace AniLingo.Web.Features.Library;

public enum SubtitleSidecarLocation
{
    EpisodeDirectory,
    SidecarDirectory,
    EpisodeSidecarDirectory
}

public sealed record SubtitleSidecarCandidate(
    string Path,
    string Format,
    bool IsJapaneseTagged,
    bool IsForced,
    bool IsHearingImpaired,
    bool IsDefault,
    SubtitleSidecarLocation Location);

// The search is bounded to the episode directory, its Subs/Subtitles directories and a
// per-episode folder inside them; unrelated folders are never scanned recursively.
public static class SubtitleSidecarLocator
{
    private static readonly string[] SidecarDirectoryNames = ["Subs", "Subtitles"];

    // SRT stays ahead of ASS/SSA because typeset ASS events often pollute learning text.
    private static readonly string[] FormatPreference = ["srt", "ass", "ssa", "vtt"];

    private static readonly char[] TokenSeparators = ['.', ' ', '_', '-', '+', '[', ']', '(', ')'];

    private static readonly HashSet<string> JapaneseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ja", "jp", "jpn", "japanese", "日本語"
    };

    private static readonly HashSet<string> OtherLanguageTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "eng", "english", "de", "deu", "ger", "german", "fr", "fra", "fre", "french",
        "es", "spa", "spanish", "it", "ita", "italian", "pt", "por", "portuguese",
        "ru", "rus", "russian", "zh", "chi", "zho", "chinese", "chs", "cht",
        "ko", "kor", "korean", "ar", "ara", "arabic", "nl", "dut", "nld", "dutch",
        "pl", "pol", "polish", "tr", "tur", "turkish", "vi", "vie", "vietnamese",
        "th", "tha", "thai", "id", "ind", "indonesian", "ms", "may", "msa", "malay"
    };

    private static readonly HashSet<string> ForcedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "forced", "foreign", "sign", "signs", "song", "songs", "karaoke"
    };

    private static readonly HashSet<string> HearingImpairedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "sdh", "cc", "hi"
    };

    public static IReadOnlyList<SubtitleSidecarCandidate> FindJapaneseCandidates(
        IEnumerable<string> mediaPaths,
        SubtitleSidecarDirectoryCache listings)
    {
        var candidates = new List<SubtitleSidecarCandidate>();

        foreach (var mediaPath in mediaPaths)
        {
            var fullPath = Path.GetFullPath(mediaPath);
            var directory = Path.GetDirectoryName(fullPath)!;
            var baseName = Path.GetFileNameWithoutExtension(fullPath);
            var episodeFiles = listings.GetFiles(directory);

            // "Show - 01.5.mkv" owns "Show - 01.5.ja.srt"; it must not be offered to "Show - 01.mkv".
            var competingBaseNames = episodeFiles
                .Where(path => LibraryScanner.MediaExtensions.Contains(Path.GetExtension(path)))
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Where(name => name.Length > baseName.Length && StartsWithBaseName(name, baseName))
                .ToArray();

            AddBaseNameMatches(
                candidates,
                episodeFiles,
                baseName,
                competingBaseNames,
                SubtitleSidecarLocation.EpisodeDirectory);

            foreach (var sidecarDirectory in FindNamedDirectories(listings, directory, SidecarDirectoryNames))
            {
                AddBaseNameMatches(
                    candidates,
                    listings.GetFiles(sidecarDirectory),
                    baseName,
                    competingBaseNames,
                    SubtitleSidecarLocation.SidecarDirectory);

                foreach (var episodeDirectory in FindNamedDirectories(listings, sidecarDirectory, [baseName]))
                {
                    foreach (var path in listings.GetFiles(episodeDirectory))
                    {
                        var candidate = Classify(
                            path,
                            baseName,
                            SubtitleSidecarLocation.EpisodeSidecarDirectory);
                        if (candidate is not null)
                        {
                            candidates.Add(candidate);
                        }
                    }
                }
            }
        }

        return Order(candidates);
    }

    // Outside a per-episode sidecar folder the file name must start with the media base name.
    // Explicitly tagged non-Japanese files are not candidates.
    public static SubtitleSidecarCandidate? Classify(
        string subtitlePath,
        string mediaBaseName,
        SubtitleSidecarLocation location)
    {
        var fileName = Path.GetFileName(subtitlePath);
        var format = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (fileName.StartsWith('.') || Array.IndexOf(FormatPreference, format) < 0)
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        string tokenText;
        if (StartsWithBaseName(stem, mediaBaseName))
        {
            tokenText = stem[mediaBaseName.Length..];
        }
        else if (location == SubtitleSidecarLocation.EpisodeSidecarDirectory)
        {
            tokenText = stem;
        }
        else
        {
            return null;
        }

        var tokens = tokenText.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
        var japanese = tokens.Any(JapaneseTokens.Contains);
        if (!japanese && tokens.Any(OtherLanguageTokens.Contains))
        {
            return null;
        }

        return new SubtitleSidecarCandidate(
            Path.GetFullPath(subtitlePath),
            format,
            japanese,
            tokens.Any(ForcedTokens.Contains),
            tokens.Any(HearingImpairedTokens.Contains),
            tokens.Any(token => token.Equals("default", StringComparison.OrdinalIgnoreCase)),
            location);
    }

    // Explicit Japanese tags win over untagged files and full subtitles over forced/signs-only
    // files; then plain before SDH, default-flagged, nearest location, format and ordinal path.
    public static IReadOnlyList<SubtitleSidecarCandidate> Order(
        IEnumerable<SubtitleSidecarCandidate> candidates) =>
        candidates
            .DistinctBy(x => x.Path, StringComparer.Ordinal)
            .OrderByDescending(x => x.IsJapaneseTagged)
            .ThenBy(x => x.IsForced)
            .ThenBy(x => x.IsHearingImpaired)
            .ThenByDescending(x => x.IsDefault)
            .ThenBy(x => x.Location)
            .ThenBy(x => Array.IndexOf(FormatPreference, x.Format))
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .ToArray();

    private static void AddBaseNameMatches(
        List<SubtitleSidecarCandidate> candidates,
        IEnumerable<string> paths,
        string baseName,
        IReadOnlyCollection<string> competingBaseNames,
        SubtitleSidecarLocation location)
    {
        foreach (var path in paths)
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (competingBaseNames.Any(name => StartsWithBaseName(stem, name)))
            {
                continue;
            }

            var candidate = Classify(path, baseName, location);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }
    }

    private static IEnumerable<string> FindNamedDirectories(
        SubtitleSidecarDirectoryCache listings,
        string parent,
        IReadOnlyCollection<string> names) =>
        listings.GetDirectories(parent)
            .Where(path => names.Contains(
                Path.GetFileName(path),
                StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

    private static bool StartsWithBaseName(string stem, string baseName) =>
        stem.StartsWith(baseName, StringComparison.OrdinalIgnoreCase) &&
        (stem.Length == baseName.Length ||
         Array.IndexOf(TokenSeparators, stem[baseName.Length]) >= 0);
}
