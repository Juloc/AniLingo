using AniLingo.Web.Features.Acquisition.Monitoring;
using AniLingo.Web.Features.Acquisition.Quality;

namespace AniLingo.Web.Features.Acquisition.Policy;

public sealed record AcquisitionDelayDecision(bool Grab, DateTimeOffset? DelayedUntilUtc, string? Reason)
{
    public static AcquisitionDelayDecision NoDelay { get; } = new(true, null, null);
}

/// <summary>
/// Pure decision logic for delay profiles (P1 item 4) and tag-scoped indexer restrictions (item 5).
/// The pipeline calls these once it has resolved the anime's tags and quality profile; nothing here
/// touches Prowlarr, SABnzbd or persisted state.
/// </summary>
public static class AcquisitionDelayEngine
{
    /// <summary>
    /// Picks the most specific matching delay profile: a profile scoped to both the quality profile
    /// and a shared tag outranks one scoped to only one of them, which outranks an unscoped
    /// (global) profile. A profile whose configured quality profile or tags do not match the anime
    /// never applies, even if marked default.
    /// </summary>
    public static AnimeDelayProfile? SelectProfile(
        IReadOnlyList<AnimeDelayProfile> profiles,
        string? qualityProfileId,
        IReadOnlyCollection<string>? tagIds)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var tags = new HashSet<string>(tagIds ?? [], StringComparer.OrdinalIgnoreCase);

        AnimeDelayProfile? best = null;
        var bestScore = -1;
        foreach (var profile in profiles)
        {
            if (profile.QualityProfileId is { Length: > 0 } scopedProfile &&
                !scopedProfile.Equals(qualityProfileId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (profile.TagIds.Length > 0 && !profile.TagIds.Any(tags.Contains))
            {
                continue;
            }

            var score = (profile.QualityProfileId is { Length: > 0 } ? 2 : 0) + (profile.TagIds.Length > 0 ? 1 : 0);
            if (score > bestScore || (score == bestScore && profile.IsDefault && best?.IsDefault != true))
            {
                best = profile;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether the candidate should be grabbed now, or held back until <c>DelayedUntilUtc</c>
    /// (unless a preferred release — one that already meets the profile's upgrade cutoff quality —
    /// appears sooner, in which case the delay is skipped).
    /// </summary>
    public static AcquisitionDelayDecision Evaluate(
        AnimeDelayProfile? delayProfile,
        AnimeQualityProfile qualityProfile,
        AnimeReleaseScoreResult candidate,
        DateTimeOffset becameWantedAtUtc,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(qualityProfile);
        ArgumentNullException.ThrowIfNull(candidate);

        if (delayProfile is null || delayProfile.DelayMinutes <= 0)
        {
            return AcquisitionDelayDecision.NoDelay;
        }

        if (AnimeMonitoringEngine.IsCutoffMet(qualityProfile, candidate))
        {
            return new(true, null, $"Preferred release quality already met; delay profile '{delayProfile.Name}' skipped.");
        }

        var delayedUntil = becameWantedAtUtc + TimeSpan.FromMinutes(delayProfile.DelayMinutes);
        if (now >= delayedUntil)
        {
            return AcquisitionDelayDecision.NoDelay;
        }

        return new(
            false,
            delayedUntil,
            $"Delayed by profile '{delayProfile.Name}' until {delayedUntil:u}, waiting for a preferred release.");
    }

    /// <summary>
    /// Indexer ids the anime's tags restrict searches to, or null when no restriction applies. Two
    /// or more applicable restrictions intersect (an anime tagged for both is limited to indexers
    /// every applicable restriction allows).
    /// </summary>
    public static int[]? RestrictedIndexerIds(
        IReadOnlyList<AnimeIndexerRestriction> restrictions,
        IReadOnlyCollection<string>? tagIds)
    {
        ArgumentNullException.ThrowIfNull(restrictions);
        var tags = new HashSet<string>(tagIds ?? [], StringComparer.OrdinalIgnoreCase);

        var applicable = restrictions
            .Where(restriction => restriction.TagIds.Length > 0 && restriction.TagIds.Any(tags.Contains))
            .ToArray();
        if (applicable.Length == 0)
        {
            return null;
        }

        IEnumerable<int> allowed = applicable[0].AllowedIndexerIds;
        foreach (var restriction in applicable.Skip(1))
        {
            allowed = allowed.Intersect(restriction.AllowedIndexerIds);
        }

        return allowed.Distinct().Order().ToArray();
    }
}
