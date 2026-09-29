using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.MediaCore;

namespace Jularr.Web.Features.Recommendations;

/// <summary>
/// The deterministic, explainable, media-neutral recommendation engine (#428). It is pure: every input
/// is in memory, it touches no provider or database, and the same inputs always produce the same shelves.
/// It reuses <see cref="RecommendationScoring"/> for content scoring (the same primitives the Books
/// surface uses, so behaviour cannot drift) and adds cross-media continuation from provider relations.
/// Visible-media-type filtering is applied to every seed, candidate and relation target so a profile only
/// ever sees rows for the media types it may browse.
/// </summary>
public static class MediaRecommendationEngine
{
    public static MediaRecommendationResult Build(
        IReadOnlyList<MediaRecommendationSeed> seeds,
        IReadOnlyList<MediaRecommendationCandidate> candidates,
        IReadOnlyList<MediaContinuationLink> continuations,
        IReadOnlySet<WorkMediaType> visibleMediaTypes,
        IReadOnlyList<RecommendationOwnedEntity> owned,
        MediaRecommendationOptions options)
    {
        var visibleSeeds = seeds
            .Where(seed => visibleMediaTypes.Contains(seed.MediaType))
            .ToArray();
        var visibleCandidates = candidates
            .Where(candidate => visibleMediaTypes.Contains(candidate.MediaType))
            .ToArray();
        var visibleContinuations = continuations
            .Where(link => visibleMediaTypes.Contains(link.Target.MediaType))
            .ToArray();

        var used = new RecommendationUsedSet();
        var shelves = new List<MediaRecommendationShelf>();

        shelves.AddRange(BuildBecauseYouShelves(
            visibleSeeds,
            visibleCandidates,
            owned,
            used,
            options));

        shelves.AddRange(BuildContinuationShelves(
            visibleContinuations,
            owned,
            used,
            options));

        return new MediaRecommendationResult(shelves);
    }

    private static IEnumerable<MediaRecommendationShelf> BuildBecauseYouShelves(
        IReadOnlyList<MediaRecommendationSeed> seeds,
        IReadOnlyList<MediaRecommendationCandidate> candidates,
        IReadOnlyList<RecommendationOwnedEntity> owned,
        RecommendationUsedSet used,
        MediaRecommendationOptions options)
    {
        var contentSeeds = seeds
            .Select(seed => (Seed: seed, Subjects: RecommendationScoring.UsefulSubjects(seed.Subjects)))
            .Where(x => x.Subjects.Count > 0 || !string.IsNullOrWhiteSpace(x.Seed.Creator))
            .OrderByDescending(x => x.Seed.IsFinished)
            .ThenByDescending(x => x.Seed.LastActivityUtc)
            .ThenBy(x => x.Seed.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, options.MaxBecauseYouShelves))
            .ToArray();

        if (contentSeeds.Length == 0)
        {
            yield break;
        }

        // Pre-extract each candidate's subjects once; reused across every seed shelf.
        var scored = candidates
            .Select(candidate => (
                Candidate: candidate,
                Subjects: RecommendationScoring.UsefulSubjects(candidate.Subjects)))
            .ToArray();

        var index = 0;
        foreach (var (seed, seedSubjects) in contentSeeds)
        {
            index++;
            var seedCreator = RecommendationScoring.CreatorTokens(seed.Creator);
            var seedIdentity = RecommendationScoring.Identity(seed.Title, seed.Creator);

            var ranked = RecommendationScoring.Rank(
                scored,
                x => x.Candidate.Id,
                x => x.Candidate.Title,
                x => x.Candidate.Creator,
                x =>
                {
                    // A candidate that is the seed itself carries no new information.
                    if (RecommendationScoring.SameEntity(
                            seedIdentity,
                            RecommendationScoring.Identity(x.Candidate.Title, x.Candidate.Creator)))
                    {
                        return null;
                    }

                    var sameCreator = seedCreator.Count > 0
                        && RecommendationScoring.CreatorsMatch(
                            seedCreator,
                            RecommendationScoring.CreatorTokens(x.Candidate.Creator));
                    var shared = RecommendationScoring.SharedSubjects(seedSubjects, x.Subjects);
                    var signal = RecommendationScoring.Score(sameCreator, shared, requireSignal: true);
                    return signal is not null && signal.Score >= options.MinimumContentScore
                        ? signal
                        : null;
                },
                owned,
                used,
                options.MaxItemsPerShelf);

            if (ranked.Count == 0)
            {
                continue;
            }

            var items = ranked
                .Select(entry => new MediaRecommendationItem(
                    entry.Item.Candidate,
                    entry.Signal.Score,
                    BuildContentReason(entry.Signal, entry.Item.Candidate.Creator, seed.Title)))
                .ToArray();

            yield return new MediaRecommendationShelf(
                MediaRecommendationShelfKind.BecauseYou,
                $"because-you-{index}",
                seed.Title,
                seed.MediaType,
                items);
        }
    }

    private static IEnumerable<MediaRecommendationShelf> BuildContinuationShelves(
        IReadOnlyList<MediaContinuationLink> continuations,
        IReadOnlyList<RecommendationOwnedEntity> owned,
        RecommendationUsedSet used,
        MediaRecommendationOptions options)
    {
        var bySeed = continuations
            .GroupBy(link => link.FromSeed.Identity?.Key ?? link.FromSeed.Title, StringComparer.Ordinal)
            .Select(group => (
                Seed: group.First().FromSeed,
                Links: group.ToArray()))
            .OrderByDescending(x => x.Seed.IsFinished)
            .ThenByDescending(x => x.Seed.LastActivityUtc)
            .ThenBy(x => x.Seed.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, options.MaxContinuationShelves))
            .ToArray();

        var index = 0;
        foreach (var (seed, links) in bySeed)
        {
            index++;
            var ordered = links
                .Select((link, rank) => (link, rank))
                .OrderBy(x => RelationGroupPriority(x.link.RelationGroupKey))
                .ThenBy(x => x.link.Target.Year ?? int.MaxValue)
                .ThenBy(x => x.rank)
                .ThenBy(x => x.link.Target.Title, StringComparer.OrdinalIgnoreCase);

            var items = new List<MediaRecommendationItem>();
            foreach (var (link, _) in ordered)
            {
                if (items.Count >= options.MaxItemsPerShelf)
                {
                    break;
                }

                var candidate = link.Target;
                var identity = RecommendationScoring.Identity(candidate.Title, candidate.Creator);
                if (IsOwned(candidate.Id, identity, owned)
                    || !used.TryAdd(candidate.Id, identity))
                {
                    continue;
                }

                items.Add(new MediaRecommendationItem(
                    candidate,
                    // Continuation is a hard relation, ranked above content matches within its shelf.
                    RelationScore(link.RelationGroupKey),
                    new MediaRecommendationReason(
                        MediaRecommendationReasonKind.Continuation,
                        [],
                        null,
                        seed.Title,
                        link.RelationGroupKey)));
            }

            if (items.Count == 0)
            {
                continue;
            }

            yield return new MediaRecommendationShelf(
                MediaRecommendationShelfKind.Continuation,
                $"continue-{index}",
                seed.Title,
                seed.MediaType,
                items);
        }
    }

    private static MediaRecommendationReason BuildContentReason(
        RecommendationSignal signal,
        string? creator,
        string seedTitle)
    {
        var subjects = signal.SharedSubjects.Select(x => x.Display).ToArray();
        var kind = signal.SameCreator
            ? subjects.Length > 0
                ? MediaRecommendationReasonKind.SameCreatorAndSubjects
                : MediaRecommendationReasonKind.SameCreator
            : MediaRecommendationReasonKind.SharedSubjects;

        return new MediaRecommendationReason(
            kind,
            subjects,
            signal.SameCreator ? creator : null,
            seedTitle,
            null);
    }

    private static int RelationGroupPriority(string relationGroupKey)
    {
        var order = FranchiseLabels.RelationGroupOrder;
        var index = 0;
        foreach (var key in order)
        {
            if (string.Equals(key, relationGroupKey, StringComparison.Ordinal))
            {
                return index;
            }

            index++;
        }

        return order.Count;
    }

    // Adaptations/sources lead, then sequels, etc.; kept high so continuation never mixes below content.
    private static int RelationScore(string relationGroupKey) =>
        1000 - RelationGroupPriority(relationGroupKey);

    private static bool IsOwned(
        string candidateId,
        RecommendationIdentity identity,
        IReadOnlyList<RecommendationOwnedEntity> owned) =>
        owned.Any(entity =>
            (!string.IsNullOrWhiteSpace(entity.CatalogId)
                && string.Equals(entity.CatalogId, candidateId, StringComparison.OrdinalIgnoreCase))
            || RecommendationScoring.SameEntity(entity.Identity, identity));
}
