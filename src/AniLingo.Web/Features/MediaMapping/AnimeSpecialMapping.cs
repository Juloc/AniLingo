namespace AniLingo.Web.Features.MediaMapping;

public sealed record RemoteAnimeSpecialPart(
    string Provider,
    string ExternalId,
    string Title,
    int EpisodeCount,
    string Format,
    string RelationType);

public sealed record AnimeSpecialMappingPlan(
    bool CanApply,
    string Reason,
    PlannedAnimeEpisodeRange? Range,
    IReadOnlyList<RemoteAnimeSpecialPart> Candidates)
{
    public static AnimeSpecialMappingPlan Blocked(
        string reason,
        IReadOnlyList<RemoteAnimeSpecialPart>? candidates = null) =>
        new(false, reason, null, candidates ?? []);
}

public static class AnimeSpecialMappingPlanner
{
    private static readonly HashSet<string> SupportedFormats =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SPECIAL",
            "OVA",
            "ONA"
        };

    private static readonly HashSet<string> SupportedRelations =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SIDE_STORY",
            "SPIN_OFF",
            "OTHER",
            "PREQUEL",
            "SEQUEL"
        };

    public static AnimeSpecialMappingPlan Plan(
        IReadOnlyList<LocalEpisodeCoordinate> localSpecialEpisodes,
        IReadOnlyList<RemoteAnimeSpecialPart> relatedMedia)
    {
        if (localSpecialEpisodes.Count == 0)
        {
            return AnimeSpecialMappingPlan.Blocked(
                "No local Season 0 specials are present.");
        }

        if (localSpecialEpisodes.Any(x =>
                x.SeasonNumber != 0 ||
                x.EpisodeNumber <= 0))
        {
            return AnimeSpecialMappingPlan.Blocked(
                "Special mapping only accepts positive Season 0 episode numbers.");
        }

        var ordered = localSpecialEpisodes
            .OrderBy(x => x.EpisodeNumber)
            .ToArray();

        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index].EpisodeNumber !=
                ordered[index - 1].EpisodeNumber + 1)
            {
                return AnimeSpecialMappingPlan.Blocked(
                    "Local Season 0 numbering contains gaps.");
            }
        }

        var candidates = relatedMedia
            .Where(x =>
                x.EpisodeCount > 0 &&
                SupportedFormats.Contains(x.Format) &&
                SupportedRelations.Contains(x.RelationType) &&
                x.EpisodeCount == ordered.Length)
            .GroupBy(
                x => (x.Provider, x.ExternalId),
                StringTupleComparer.Instance)
            .Select(group => group.First())
            .ToArray();

        if (candidates.Length == 0)
        {
            return AnimeSpecialMappingPlan.Blocked(
                "No related AniList SPECIAL/OVA/ONA entry has the same episode count as local Season 0.");
        }

        if (candidates.Length > 1)
        {
            return AnimeSpecialMappingPlan.Blocked(
                "Multiple related AniList SPECIAL/OVA/ONA entries fit local Season 0.",
                candidates);
        }

        var candidate = candidates[0];
        var range = new PlannedAnimeEpisodeRange(
            SeasonNumber: 0,
            LocalEpisodeStart: ordered[0].EpisodeNumber,
            LocalEpisodeEnd: ordered[^1].EpisodeNumber,
            RemoteEpisodeStart: 1,
            new RemoteAnimePart(
                candidate.Provider,
                candidate.ExternalId,
                candidate.Title,
                candidate.EpisodeCount));

        return new AnimeSpecialMappingPlan(
            true,
            $"Season 0 maps uniquely to AniList {candidate.Format} via {candidate.RelationType}.",
            range,
            candidates);
    }

    private sealed class StringTupleComparer :
        IEqualityComparer<(string Provider, string ExternalId)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals(
            (string Provider, string ExternalId) x,
            (string Provider, string ExternalId) y) =>
            string.Equals(
                x.Provider,
                y.Provider,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                x.ExternalId,
                y.ExternalId,
                StringComparison.Ordinal);

        public int GetHashCode(
            (string Provider, string ExternalId) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Provider),
                StringComparer.Ordinal.GetHashCode(obj.ExternalId));
    }
}
