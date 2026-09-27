using System.Globalization;
using System.Text.RegularExpressions;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Acquisition.Prowlarr;

namespace Jularr.Web.Features.ReadingAcquisition;

public enum ReadingReleaseFormat
{
    Unknown,
    Epub,
    Pdf,
    Cbz,
    Zip,
    Cbr
}

public sealed record ReadingReleaseInfo(
    ReadingReleaseFormat Format,
    int? VolumeNumber,
    double? ChapterStart,
    double? ChapterEnd,
    string? Language,
    bool IsCompleteOrBatch);

public sealed record ReadingAcquisitionTarget(
    MediaAcquisitionKind Kind,
    string Title,
    IReadOnlyList<string> Aliases,
    string? Author = null,
    int? RequestedVolume = null,
    double? RequestedChapterStart = null,
    double? RequestedChapterEnd = null,
    IReadOnlyList<string>? PreferredLanguages = null);

public sealed record RankedReadingRelease(
    ProwlarrReleaseCandidate Release,
    ReadingReleaseInfo Parsed,
    int Score,
    string? RejectedBecause);

public sealed record ReadingUsenetSearchResult(
    IReadOnlyList<string> Queries,
    IReadOnlyList<RankedReadingRelease> Ranked,
    IReadOnlyList<IndexerSearchWarning> Warnings,
    bool UsedCategoryFallback)
{
    public ProwlarrReleaseCandidate? Picked =>
        Ranked.FirstOrDefault(candidate => candidate.Score > 0)?.Release;

    public string FailureMessage =>
        Ranked.Count == 0
            ? Warnings.Count > 0
                ? $"No release found on the indexers ({Warnings[0].IndexerName}: {Warnings[0].Message})."
                : "No release found on the indexers."
            : "No suitable release matched the requested title, format, volume, chapter or language.";
}

public static partial class ReadingReleaseParser
{
    [GeneratedRegex(@"(?:^|[\s._\-\[\(])(?:vol(?:ume)?\.?\s*|v)(?<value>\d{1,3})(?:\b|[\s._\-\]\)])", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeRegex();

    [GeneratedRegex(@"(?:^|[\s._\-\[\(])(?:ch(?:apter)?\.?\s*|c)(?<start>\d{1,4}(?:\.\d+)?)(?:\s*(?:-|–|—|to)\s*(?:ch(?:apter)?\.?\s*|c)?(?<end>\d{1,4}(?:\.\d+)?))?", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterRegex();

    [GeneratedRegex(@"(?:^|[\s._\-\[\(])(?<start>\d{1,3})\s*(?:-|–|—|to)\s*(?<end>\d{1,3})(?:\s*(?:vol(?:ume)?s?|v))?(?:\b|[\s._\-\]\)])", RegexOptions.IgnoreCase)]
    private static partial Regex BareRangeRegex();

    public static ReadingReleaseInfo Parse(string? title)
    {
        var value = title ?? string.Empty;
        var lower = value.ToLowerInvariant();
        var format = lower.Contains("epub", StringComparison.Ordinal)
            ? ReadingReleaseFormat.Epub
            : lower.Contains("cbz", StringComparison.Ordinal)
                ? ReadingReleaseFormat.Cbz
                : lower.Contains("cbr", StringComparison.Ordinal)
                    ? ReadingReleaseFormat.Cbr
                    : lower.Contains("pdf", StringComparison.Ordinal)
                        ? ReadingReleaseFormat.Pdf
                        : ContainsToken(lower, "zip")
                            ? ReadingReleaseFormat.Zip
                            : ReadingReleaseFormat.Unknown;

        int? volume = null;
        var volumeMatch = VolumeRegex().Match(value);
        if (volumeMatch.Success &&
            int.TryParse(volumeMatch.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVolume))
        {
            volume = parsedVolume;
        }

        double? chapterStart = null;
        double? chapterEnd = null;
        var chapterMatch = ChapterRegex().Match(value);
        if (chapterMatch.Success &&
            double.TryParse(chapterMatch.Groups["start"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedChapter))
        {
            chapterStart = parsedChapter;
            chapterEnd = parsedChapter;
            if (chapterMatch.Groups["end"].Success &&
                double.TryParse(chapterMatch.Groups["end"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedEnd))
            {
                chapterEnd = parsedEnd;
            }
        }

        var language = DetectLanguage(lower);
        var completeOrBatch =
            ContainsToken(lower, "complete") ||
            ContainsToken(lower, "completed") ||
            ContainsToken(lower, "batch") ||
            lower.Contains("all volumes", StringComparison.Ordinal) ||
            lower.Contains("volumes 1-", StringComparison.Ordinal) ||
            lower.Contains("vol 1-", StringComparison.Ordinal);

        if (!completeOrBatch)
        {
            var range = BareRangeRegex().Match(value);
            completeOrBatch = range.Success &&
                int.TryParse(range.Groups["start"].Value, out var start) &&
                int.TryParse(range.Groups["end"].Value, out var end) &&
                end > start;
        }

        return new ReadingReleaseInfo(
            format,
            volume,
            chapterStart,
            chapterEnd,
            language,
            completeOrBatch);
    }

    private static string? DetectLanguage(string lower)
    {
        if (ContainsAnyToken(lower, "german", "deutsch", "ger", "de-de"))
        {
            return "de";
        }

        if (ContainsAnyToken(lower, "english", "eng", "en-us", "en-gb"))
        {
            return "en";
        }

        if (ContainsAnyToken(lower, "japanese", "jpn", "japanese raw", "raw", "ja-jp"))
        {
            return "ja";
        }

        return null;
    }

    private static bool ContainsAnyToken(string value, params string[] tokens) =>
        tokens.Any(token => ContainsToken(value, token));

    private static bool ContainsToken(string value, string token)
    {
        var index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
            var end = index + token.Length;
            var after = end >= value.Length || !char.IsLetterOrDigit(value[end]);
            if (before && after)
            {
                return true;
            }

            index = value.IndexOf(token, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}

public static class ReadingReleaseSelector
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "of", "and", "or", "der", "die", "das", "und", "des", "den",
        "le", "la", "les", "de", "no", "to"
    };

    public static IReadOnlyList<RankedReadingRelease> Rank(
        IReadOnlyList<ProwlarrReleaseCandidate> releases,
        ReadingAcquisitionTarget target) =>
        releases
            .Select(release => Judge(release, target))
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Release.PublishedAt)
            .ThenBy(candidate => candidate.Release.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static RankedReadingRelease Judge(
        ProwlarrReleaseCandidate release,
        ReadingAcquisitionTarget target)
    {
        var parsed = ReadingReleaseParser.Parse(release.Title);

        if (release.InternalDownloadUri is null)
        {
            return Reject(release, parsed, "no download link");
        }

        if (release.Protocol is not null &&
            !release.Protocol.Equals("usenet", StringComparison.OrdinalIgnoreCase))
        {
            return Reject(release, parsed, "not a Usenet release");
        }

        var names = new[] { target.Title }
            .Concat(target.Aliases ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!names.Any(name => TitleMatches(release.Title, name)))
        {
            return Reject(release, parsed, "title does not match");
        }

        if (target.Kind == MediaAcquisitionKind.Manga)
        {
            if (parsed.Format is ReadingReleaseFormat.Epub or ReadingReleaseFormat.Pdf or ReadingReleaseFormat.Cbr)
            {
                return Reject(release, parsed, "format is not supported by the Manga importer");
            }
        }
        else if (target.Kind == MediaAcquisitionKind.LightNovel)
        {
            if (parsed.Format is ReadingReleaseFormat.Pdf or ReadingReleaseFormat.Cbz or ReadingReleaseFormat.Cbr)
            {
                return Reject(release, parsed, "format is not supported by the Light Novel importer");
            }
        }
        else
        {
            return Reject(release, parsed, "unsupported reading media type");
        }

        if (target.RequestedVolume is { } requestedVolume &&
            parsed.VolumeNumber is { } releaseVolume &&
            requestedVolume != releaseVolume)
        {
            return Reject(release, parsed, $"volume {releaseVolume} does not match requested volume {requestedVolume}");
        }

        if (target.RequestedChapterStart is { } requestedChapter &&
            parsed.ChapterStart is { } releaseChapterStart)
        {
            var releaseChapterEnd = parsed.ChapterEnd ?? releaseChapterStart;
            if (requestedChapter < releaseChapterStart || requestedChapter > releaseChapterEnd)
            {
                return Reject(release, parsed, "chapter range does not contain the requested chapter");
            }
        }

        var preferredLanguages = target.PreferredLanguages?
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Select(NormalizeLanguage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (preferredLanguages.Length > 0 &&
            parsed.Language is { } releaseLanguage &&
            !preferredLanguages.Contains(releaseLanguage, StringComparer.OrdinalIgnoreCase))
        {
            return Reject(release, parsed, $"release language '{releaseLanguage}' is not allowed");
        }

        var score = 100;
        score += target.Kind switch
        {
            MediaAcquisitionKind.Manga => parsed.Format switch
            {
                ReadingReleaseFormat.Cbz => 35,
                ReadingReleaseFormat.Zip => 24,
                ReadingReleaseFormat.Unknown => 8,
                _ => 0
            },
            MediaAcquisitionKind.LightNovel => parsed.Format switch
            {
                ReadingReleaseFormat.Epub => 40,
                ReadingReleaseFormat.Zip => 15,
                ReadingReleaseFormat.Unknown => 8,
                _ => 0
            },
            _ => 0
        };

        if (target.RequestedVolume is { } volume && parsed.VolumeNumber == volume)
        {
            score += 24;
        }
        else if (target.RequestedVolume is null && parsed.IsCompleteOrBatch)
        {
            score += 12;
        }

        if (target.RequestedChapterStart is { } chapter &&
            parsed.ChapterStart is { } chapterStart &&
            chapter >= chapterStart &&
            chapter <= (parsed.ChapterEnd ?? chapterStart))
        {
            score += 20;
        }

        if (preferredLanguages.Length > 0 && parsed.Language is { } language)
        {
            var languageIndex = Array.FindIndex(
                preferredLanguages,
                item => item.Equals(language, StringComparison.OrdinalIgnoreCase));
            score += Math.Max(2, 12 - languageIndex * 3);
        }

        if (!string.IsNullOrWhiteSpace(target.Author) &&
            Words(release.Title).Overlaps(Words(target.Author)))
        {
            score += 5;
        }

        if (release.SizeBytes is > 0)
        {
            if (target.Kind == MediaAcquisitionKind.LightNovel && release.SizeBytes > 2L * 1024 * 1024 * 1024)
            {
                score -= 20;
            }

            if (target.Kind == MediaAcquisitionKind.Manga && release.SizeBytes > 100L * 1024 * 1024 * 1024)
            {
                score -= 10;
            }
        }

        return new RankedReadingRelease(release, parsed, Math.Max(score, 1), null);
    }

    private static RankedReadingRelease Reject(
        ProwlarrReleaseCandidate release,
        ReadingReleaseInfo parsed,
        string reason) =>
        new(release, parsed, 0, reason);

    internal static bool TitleMatches(string releaseTitle, string expectedTitle)
    {
        var compactExpected = Compact(expectedTitle);
        var compactRelease = Compact(releaseTitle);
        if (compactExpected.Length >= 4 &&
            compactRelease.Contains(compactExpected, StringComparison.Ordinal))
        {
            return true;
        }

        var expectedWords = Words(expectedTitle);
        if (expectedWords.Count == 0)
        {
            return false;
        }

        var releaseWords = Words(releaseTitle);
        return expectedWords.All(releaseWords.Contains);
    }

    internal static HashSet<string> Words(string? value)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return words;
        }

        foreach (var part in value.Split(
                     [' ', '.', '_', '-', ':', ';', ',', '(', ')', '[', ']', '{', '}', '\'', '"', '!', '?', '&', '/', '\\', '+'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = new string(part
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
            if (normalized.Length > 1 && !StopWords.Contains(normalized))
            {
                words.Add(normalized);
            }
        }

        return words;
    }

    private static string Compact(string? value) =>
        string.Concat((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant));

    private static string NormalizeLanguage(string value)
    {
        var lower = value.Trim().ToLowerInvariant();
        if (lower.StartsWith("de", StringComparison.Ordinal))
        {
            return "de";
        }

        if (lower.StartsWith("en", StringComparison.Ordinal))
        {
            return "en";
        }

        if (lower.StartsWith("ja", StringComparison.Ordinal) ||
            lower.StartsWith("jp", StringComparison.Ordinal))
        {
            return "ja";
        }

        return lower;
    }
}

public static class ReadingUsenetSearch
{
    private static readonly int[] LightNovelCategories = [7020, 7000];
    private static readonly int[] MangaCategories = [7030, 7000];

    public static IReadOnlyList<string> Queries(ReadingAcquisitionTarget target)
    {
        var queries = new List<string>();
        var names = new[] { target.Title }
            .Concat(target.Aliases ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(target.Author) &&
                target.Kind == MediaAcquisitionKind.LightNovel)
            {
                queries.Add($"{target.Author.Trim()} {name.Trim()}");
            }

            if (target.RequestedVolume is { } volume)
            {
                queries.Add($"{name.Trim()} vol {volume}");
                queries.Add($"{name.Trim()} volume {volume}");
            }
            else if (target.RequestedChapterStart is { } chapter)
            {
                queries.Add($"{name.Trim()} ch {chapter.ToString(CultureInfo.InvariantCulture)}");
            }

            queries.Add(name.Trim());
        }

        return queries
            .Where(query => query.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    public static async Task<ReadingUsenetSearchResult> SearchAsync(
        IndexerSearchCoordinator indexers,
        ReadingAcquisitionTarget target,
        CancellationToken cancellationToken)
    {
        var queries = Queries(target);
        var result = await indexers.SearchCategoriesAsync(
            queries,
            entry => Categories(entry, target.Kind),
            cancellationToken);

        var fallback = false;
        if (result.Releases.Count == 0)
        {
            var anyCategory = await indexers.SearchCategoriesAsync(
                queries,
                _ => [],
                cancellationToken);
            if (anyCategory.Releases.Count > 0)
            {
                result = anyCategory;
                fallback = true;
            }
        }

        return new ReadingUsenetSearchResult(
            queries,
            ReadingReleaseSelector.Rank(result.Releases, target),
            result.Warnings,
            fallback);
    }

    public static IReadOnlyList<int> Categories(
        IndexerEntry entry,
        MediaAcquisitionKind kind)
    {
        var defaults = kind == MediaAcquisitionKind.Manga
            ? MangaCategories
            : LightNovelCategories;

        return entry.Settings.EffectiveBookCategories
            .Concat(defaults)
            .Distinct()
            .ToArray();
    }
}
