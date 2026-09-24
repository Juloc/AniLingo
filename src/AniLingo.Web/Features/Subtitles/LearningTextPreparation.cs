using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Subtitles;

public enum LearningTextFallbackStage
{
    LocalSubtitle,
    EmbeddedSubtitle,
    Jimaku,
    Whisper
}

public enum LearningTextPreparationStatus
{
    None,
    Queued,
    Processing,
    Ready,
    Failed
}

public enum LearningTextSourceKind
{
    Existing,
    LocalSubtitle,
    EmbeddedSubtitle,
    Jimaku,
    Whisper
}

public sealed record LearningTextPreparationState(
    LearningTextPreparationStatus Status,
    LearningTextSourceKind? Source = null,
    string? Message = null,
    DateTimeOffset? UpdatedAt = null)
{
    public static LearningTextPreparationState Empty { get; } =
        new(LearningTextPreparationStatus.None);
}

public sealed record LearningTextCoverageSnapshot(
    int TotalEpisodes,
    int ReadyEpisodes,
    int QueuedEpisodes,
    int ProcessingEpisodes,
    int FailedEpisodes,
    int MissingEpisodes);

public sealed record LearningTextEpisodeStatus(
    Guid EpisodeId,
    string AnimeTitle,
    int SeasonNumber,
    int EpisodeNumber,
    string EpisodeTitle,
    LearningTextPreparationState State);

public sealed record JimakuSubtitleFileCandidate(
    string Name,
    string Url,
    DateTimeOffset? LastModified = null);

public static class LearningTextFallbackPolicy
{
    private static readonly LearningTextFallbackStage[] WithoutJimaku =
    [
        LearningTextFallbackStage.LocalSubtitle,
        LearningTextFallbackStage.EmbeddedSubtitle,
        LearningTextFallbackStage.Whisper
    ];

    private static readonly LearningTextFallbackStage[] WithJimaku =
    [
        LearningTextFallbackStage.LocalSubtitle,
        LearningTextFallbackStage.EmbeddedSubtitle,
        LearningTextFallbackStage.Jimaku,
        LearningTextFallbackStage.Whisper
    ];

    public static IReadOnlyList<LearningTextFallbackStage> Build(bool jimakuConfigured) =>
        jimakuConfigured ? WithJimaku : WithoutJimaku;
}

public static class JimakuSubtitleMatcher
{
    private static readonly Regex ExplicitEpisodeRegex = new(
        @"(?i)(?:s\d{1,2}[ ._-]*)?e(?<episode>\d{1,4})(?:v\d+)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LooseEpisodeRegex = new(
        @"(?i)(?:^|[\s._\-\[\(])#?(?<episode>\d{1,4})(?:v\d+)?(?=$|[\s._\-\]\)])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static JimakuSubtitleFileCandidate? SelectBest(
        IEnumerable<JimakuSubtitleFileCandidate> files,
        int episodeNumber,
        bool episodeFiltered)
    {
        return files
            .Select(file => new
            {
                File = file,
                Score = Score(file.Name, episodeNumber, episodeFiltered)
            })
            .Where(x => x.Score > int.MinValue)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.File.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.File)
            .FirstOrDefault();
    }

    public static int Score(
        string fileName,
        int episodeNumber,
        bool episodeFiltered)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var formatScore = extension switch
        {
            ".ass" => 400,
            ".ssa" => 350,
            ".srt" => 300,
            ".zip" => 100,
            _ => int.MinValue
        };

        if (formatScore == int.MinValue)
        {
            return int.MinValue;
        }

        if (!episodeFiltered && !MatchesEpisode(fileName, episodeNumber))
        {
            return int.MinValue;
        }

        var score = formatScore + (episodeFiltered ? 1000 : 800);

        if (fileName.Contains("sign", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("song", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("karaoke", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("forced", StringComparison.OrdinalIgnoreCase))
        {
            score -= 500;
        }

        if (fileName.Contains("ai", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("whisper", StringComparison.OrdinalIgnoreCase))
        {
            score -= 100;
        }

        return score;
    }

    public static bool MatchesEpisode(string fileName, int episodeNumber)
    {
        var explicitMatches = ExplicitEpisodeRegex.Matches(fileName);
        if (explicitMatches.Count > 0)
        {
            return explicitMatches.Any(match =>
                int.TryParse(match.Groups["episode"].Value, out var parsed) &&
                parsed == episodeNumber);
        }

        return LooseEpisodeRegex.Matches(fileName).Any(match =>
            int.TryParse(match.Groups["episode"].Value, out var parsed) &&
            parsed == episodeNumber);
    }
}
