using AniLingo.Web.Features.Acquisition;

namespace AniLingo.Web.Features.Acquisition.Quality;

public enum AnimeReleaseRuleField
{
    RawTitle,
    ReleaseGroup,
    Source,
    Resolution,
    VideoCodec,
    BitDepth,
    HdrFormat,
    AudioCodec,
    AudioLanguage,
    SubtitleLanguage,
    DualAudio,
    MultiAudio,
    Proper,
    Repack
}

public enum AnimeReleaseRuleMatch
{
    Equals,
    Contains,
    Regex
}

public sealed record AnimeReleaseScoreRule(
    string Name,
    AnimeReleaseRuleField Field,
    AnimeReleaseRuleMatch Match,
    string Value,
    int Score);

public sealed record AnimeQualityProfile(
    string Id,
    string Name,
    string[] AllowedQualities,
    string[] QualityOrder,
    bool UpgradeAllowed,
    string? UpgradeCutoffQuality,
    int MinimumScore,
    long? MinimumSizeBytes,
    long? MaximumSizeBytes,
    string[] MustContain,
    string[] MustNotContain,
    string[] RequiredRegex,
    string[] RejectedRegex,
    AnimeReleaseScoreRule[] ScoreRules);

public sealed record AnimeQualityProfileState(
    int Version,
    string DefaultProfileId,
    AnimeQualityProfile[] Profiles,
    Dictionary<string, string> AnimeProfileAssignments);

public sealed record AnimeReleaseCandidate(
    AnimeReleaseInfo Release,
    long? SizeBytes = null,
    string? Indexer = null,
    string? SourceId = null);

public sealed record AnimeReleaseScoreResult(
    AnimeReleaseCandidate Candidate,
    bool Accepted,
    int Score,
    string QualityKey,
    int QualityRank,
    IReadOnlyList<string> RejectionReasons,
    IReadOnlyList<string> ScoreReasons);

public static class AnimeReleaseQuality
{
    public static string GetKey(AnimeReleaseInfo release)
    {
        var source = release.Source switch
        {
            AnimeReleaseSource.WebDl or AnimeReleaseSource.WebRip => "WEB",
            AnimeReleaseSource.BluRay or AnimeReleaseSource.BluRayRip => "BLURAY",
            AnimeReleaseSource.Hdtv => "HDTV",
            _ => "UNKNOWN"
        };

        var resolution = release.Resolution is > 0
            ? $"{release.Resolution}p"
            : "UNKNOWN";

        return $"{source}-{resolution}";
    }
}

public static class AnimeQualityProfiles
{
    public const string DefaultAnime1080pId = "anime-1080p";

    public static AnimeQualityProfile CreateDefaultAnime1080p() =>
        new(
            DefaultAnime1080pId,
            "Anime 1080p",
            [
                "BLURAY-1080p",
                "WEB-1080p",
                "HDTV-1080p",
                "BLURAY-720p",
                "WEB-720p",
                "HDTV-720p"
            ],
            [
                "BLURAY-1080p",
                "WEB-1080p",
                "HDTV-1080p",
                "BLURAY-720p",
                "WEB-720p",
                "HDTV-720p"
            ],
            UpgradeAllowed: true,
            UpgradeCutoffQuality: "BLURAY-1080p",
            MinimumScore: 0,
            MinimumSizeBytes: null,
            MaximumSizeBytes: null,
            MustContain: [],
            MustNotContain: [],
            RequiredRegex: [],
            RejectedRegex: [],
            ScoreRules:
            [
                new(
                    "Prefer Proper",
                    AnimeReleaseRuleField.Proper,
                    AnimeReleaseRuleMatch.Equals,
                    "true",
                    5),
                new(
                    "Prefer Repack",
                    AnimeReleaseRuleField.Repack,
                    AnimeReleaseRuleMatch.Equals,
                    "true",
                    5)
            ]);

    public static AnimeQualityProfileState CreateDefaultState() =>
        new(
            Version: 1,
            DefaultProfileId: DefaultAnime1080pId,
            Profiles: [CreateDefaultAnime1080p()],
            AnimeProfileAssignments: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
