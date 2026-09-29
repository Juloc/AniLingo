using Jularr.Web.Features.MediaCore;
using Jularr.Web.Features.Watchlist;

namespace Jularr.Web.Features.Recommendations;

/// <summary>
/// A work the profile has engaged with, used as a recommendation seed. Subjects and creator drive
/// content scoring (only books and light novels carry them today); the identity lets the engine follow
/// cross-media relations. Anime and manga seeds carry an identity but usually no subjects, so they seed
/// continuation shelves rather than subject shelves.
/// </summary>
public sealed record MediaRecommendationSeed(
    WorkMediaType MediaType,
    WatchlistIdentity? Identity,
    string Title,
    string? Creator,
    IReadOnlyList<string> Subjects,
    bool IsFinished,
    DateTime LastActivityUtc);

/// <summary>
/// A recommendable work with everything needed both to score it (subjects/creator/identity) and to
/// render it on the shared shelf surface (title, cover, link, year). Candidates come from the local
/// library, followed works and resolved relation targets; the engine never fabricates one.
/// </summary>
public sealed record MediaRecommendationCandidate(
    string Id,
    WorkMediaType MediaType,
    WatchlistIdentity? Identity,
    string Title,
    string? Creator,
    IReadOnlyList<string> Subjects,
    string? CoverImageUrl,
    string Href,
    int? Year,
    bool IsLocal = false,
    string? CatalogId = null);

/// <summary>
/// A provider relation from one of the profile's works to another work, resolved to a renderable
/// candidate. <see cref="RelationGroupKey"/> is a <see cref="Jularr.Web.Features.Franchises.FranchiseLabels"/>
/// group key so the strongest relations (adaptations, sequels) can lead.
/// </summary>
public sealed record MediaContinuationLink(
    MediaRecommendationSeed FromSeed,
    MediaRecommendationCandidate Target,
    string RelationGroupKey);

public enum MediaRecommendationShelfKind
{
    /// <summary>Content match: candidates sharing subjects and/or a creator with one seed.</summary>
    BecauseYou,

    /// <summary>Cross-media continuation: the adaptation, source or next entry of a followed work.</summary>
    Continuation
}

public enum MediaRecommendationReasonKind
{
    SameCreator,
    SharedSubjects,
    SameCreatorAndSubjects,
    Continuation
}

/// <summary>
/// The explainable, localization-ready basis for one recommendation. The surface turns this into copy;
/// it never carries baked display text so the same reason renders in any culture.
/// </summary>
public sealed record MediaRecommendationReason(
    MediaRecommendationReasonKind Kind,
    IReadOnlyList<string> SharedSubjects,
    string? Creator,
    string? SeedTitle,
    string? RelationGroupKey);

public sealed record MediaRecommendationItem(
    MediaRecommendationCandidate Candidate,
    int Score,
    MediaRecommendationReason Reason);

/// <summary>
/// One recommendation row. <see cref="SeedTitle"/> and <see cref="RelationGroupKey"/> let the surface
/// build an explainable heading ("Because you enjoyed …", "Because you follow …") without the engine
/// choosing wording.
/// </summary>
public sealed record MediaRecommendationShelf(
    MediaRecommendationShelfKind Kind,
    string Id,
    string? SeedTitle,
    WorkMediaType? MediaType,
    IReadOnlyList<MediaRecommendationItem> Items);

public sealed record MediaRecommendationResult(
    IReadOnlyList<MediaRecommendationShelf> Shelves)
{
    public static MediaRecommendationResult Empty { get; } = new([]);

    public bool IsEmpty => Shelves.Count == 0;
}

public sealed class MediaRecommendationOptions
{
    public static MediaRecommendationOptions Default { get; } = new();

    /// <summary>How many seeds produce their own "Because you enjoyed …" shelf.</summary>
    public int MaxBecauseYouShelves { get; init; } = 3;

    /// <summary>How many seeds produce their own continuation shelf.</summary>
    public int MaxContinuationShelves { get; init; } = 3;

    public int MaxItemsPerShelf { get; init; } = 12;

    /// <summary>Minimum content score (creator/subject) for a candidate to appear on a "Because you" shelf.</summary>
    public int MinimumContentScore { get; init; } = 1;
}
