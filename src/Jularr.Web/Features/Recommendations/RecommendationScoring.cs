using System.Text.RegularExpressions;

namespace Jularr.Web.Features.Recommendations;

/// <summary>
/// One profile subject/genre/theme reduced to a normalized match key, its display form and the
/// significant tokens used for subset matching. Shared by every media type: for books these are
/// Open Library subjects, for anime/manga/light novels they are provider genres and tags.
/// </summary>
public sealed record RecommendationSubject(
    string Key,
    string Display,
    IReadOnlySet<string> Tokens);

/// <summary>
/// The explainable outcome of scoring one candidate: its numeric score, whether it shares a creator
/// (author/studio) with the seed and which profile subjects it shares. The surface turns this into
/// localized reason copy; the engine never bakes display text into the score itself.
/// </summary>
public sealed record RecommendationSignal(
    int Score,
    bool SameCreator,
    IReadOnlyList<RecommendationSubject> SharedSubjects);

/// <summary>Stable identity of a candidate for owned/duplicate suppression: normalized main title plus creator tokens.</summary>
public sealed record RecommendationIdentity(
    string TitleKey,
    IReadOnlySet<string> CreatorTokens);

/// <summary>A work already in the profile's library, matched by catalog id where present and by identity otherwise.</summary>
public sealed record RecommendationOwnedEntity(
    RecommendationIdentity Identity,
    string? CatalogId);

/// <summary>
/// The media-type-agnostic scoring framework behind every recommendation surface (#428). It carries
/// no state and touches no provider or database: callers extract creators/subjects from their own
/// records, and this class turns them into a subject profile, scores candidates against seeds or the
/// profile, and ranks the survivors with owned/duplicate suppression. Books (#371) and the cross-media
/// engine share these exact primitives so their behaviour cannot drift.
/// </summary>
public static partial class RecommendationScoring
{
    /// <summary>Default weight for a candidate sharing the seed's creator (author, studio, …).</summary>
    public const int SameCreatorScore = 100;

    /// <summary>Default weight per shared profile subject.</summary>
    public const int SubjectOverlapScore = 15;

    /// <summary>How many shared subjects are counted before the bonus saturates.</summary>
    public const int MaxCountedSubjectOverlap = 4;

    private static readonly HashSet<string> DefaultStopTokens = new(
        [
            "fiction",
            "general",
            "literature",
            "novel",
            "novels",
            "book",
            "books",
            "and",
            "the",
            "of",
            "in",
            "a",
            "an"
        ],
        StringComparer.Ordinal);

    /// <summary>Generic tokens dropped from subject tokens. Callers may extend this per media type.</summary>
    public static IReadOnlySet<string> DefaultSubjectStopTokens => DefaultStopTokens;

    /// <summary>
    /// Turns raw subject/genre strings into normalized, de-duplicated <see cref="RecommendationSubject"/>
    /// values. <paramref name="genericSubjects"/> (normalized keys) and <paramref name="stopTokens"/>
    /// let each media type drop its own noise (Open Library shelf tags, "fiction", …) without changing
    /// the shared matching logic.
    /// </summary>
    public static IReadOnlyList<RecommendationSubject> UsefulSubjects(
        IReadOnlyList<string> subjects,
        IReadOnlySet<string>? genericSubjects = null,
        IReadOnlySet<string>? stopTokens = null)
    {
        genericSubjects ??= System.Collections.Immutable.ImmutableHashSet<string>.Empty;
        stopTokens ??= DefaultStopTokens;

        var result = new List<RecommendationSubject>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in subjects)
        {
            var display = raw?.Trim();
            if (string.IsNullOrWhiteSpace(display)
                || display.Length > 80
                || display.Contains(':')
                || display.Contains('='))
            {
                continue;
            }

            var key = NormalizeForMatch(display);
            if (key.Length < 3
                || genericSubjects.Contains(key)
                || key.StartsWith("reading level", StringComparison.Ordinal)
                || !keys.Add(key))
            {
                continue;
            }

            var tokens = key
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Length >= 2 && !stopTokens.Contains(x))
                .ToHashSet(StringComparer.Ordinal);

            if (tokens.Count > 0)
            {
                result.Add(new RecommendationSubject(key, display, tokens));
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the weighted subject profile from a set of seeds' subject lists, in profile order:
    /// most repeated subjects first, ties broken by first appearance.
    /// </summary>
    public static IReadOnlyList<RecommendationSubject> BuildSubjectProfile(
        IEnumerable<IReadOnlyList<RecommendationSubject>> seedSubjects)
    {
        var weights = new Dictionary<string, (RecommendationSubject Subject, int Weight, int FirstSeen)>(
            StringComparer.Ordinal);
        var position = 0;

        foreach (var subjects in seedSubjects)
        {
            foreach (var subject in subjects)
            {
                weights[subject.Key] = weights.TryGetValue(subject.Key, out var current)
                    ? current with { Weight = current.Weight + 1 }
                    : (subject, 1, position++);
            }
        }

        return weights.Values
            .OrderByDescending(x => x.Weight)
            .ThenBy(x => x.FirstSeen)
            .Select(x => x.Subject)
            .ToArray();
    }

    /// <summary>The profile subjects (in profile order) shared by the candidate's already-extracted subjects.</summary>
    public static IReadOnlyList<RecommendationSubject> SharedSubjects(
        IReadOnlyList<RecommendationSubject> profile,
        IReadOnlyList<RecommendationSubject> candidateSubjects)
    {
        if (profile.Count == 0 || candidateSubjects.Count == 0)
        {
            return [];
        }

        return profile
            .Where(subject => candidateSubjects.Any(candidate =>
                SubjectsMatch(subject.Tokens, candidate.Tokens)))
            .ToArray();
    }

    /// <summary>
    /// Additive score: a shared creator counts <paramref name="sameCreatorScore"/>, each shared subject
    /// <paramref name="subjectOverlapScore"/> up to <paramref name="maxCountedOverlap"/>. When
    /// <paramref name="requireSignal"/> is set, a zero score yields <c>null</c> (no evidence, no card).
    /// </summary>
    public static RecommendationSignal? Score(
        bool sameCreator,
        IReadOnlyList<RecommendationSubject> shared,
        bool requireSignal,
        int sameCreatorScore = SameCreatorScore,
        int subjectOverlapScore = SubjectOverlapScore,
        int maxCountedOverlap = MaxCountedSubjectOverlap)
    {
        var score =
            (sameCreator ? sameCreatorScore : 0)
            + Math.Min(shared.Count, maxCountedOverlap) * subjectOverlapScore;

        if (requireSignal && score == 0)
        {
            return null;
        }

        return new RecommendationSignal(score, sameCreator, shared);
    }

    /// <summary>
    /// Ranks candidates by score (then their input order, then title, then id), dropping owned works and
    /// works already shown, and returns at most <paramref name="maxItems"/> survivors with their signal.
    /// Accessors keep this generic over any candidate record.
    /// </summary>
    public static IReadOnlyList<(T Item, RecommendationSignal Signal)> Rank<T>(
        IReadOnlyList<T> candidates,
        Func<T, string> id,
        Func<T, string> title,
        Func<T, string?> creator,
        Func<T, RecommendationSignal?> scorer,
        IReadOnlyList<RecommendationOwnedEntity> owned,
        RecommendationUsedSet used,
        int maxItems)
    {
        var ordered = candidates
            .Select((item, rank) => (
                Item: item,
                Rank: rank,
                Id: id(item),
                Identity: Identity(title(item), creator(item)),
                Signal: scorer(item)))
            .Where(x =>
                x.Signal is not null
                && !IsOwned(x.Id, x.Identity, owned))
            .OrderByDescending(x => x.Signal!.Score)
            .ThenBy(x => x.Rank)
            .ThenBy(x => title(x.Item), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.Ordinal);

        var picked = new List<(T, RecommendationSignal)>();
        foreach (var candidate in ordered)
        {
            if (picked.Count >= maxItems)
            {
                break;
            }

            if (!used.TryAdd(candidate.Id, candidate.Identity))
            {
                continue;
            }

            picked.Add((candidate.Item, candidate.Signal!));
        }

        return picked;
    }

    public static RecommendationIdentity Identity(string title, string? creator) =>
        new(TitleKey(title), CreatorTokens(creator));

    public static string TitleKey(string title)
    {
        var main = title;
        var cut = main.IndexOfAny([':', '(', ';', '[']);
        if (cut > 0)
        {
            main = main[..cut];
        }

        var key = NormalizeForMatch(main);
        foreach (var article in (string[])["the ", "a ", "an "])
        {
            if (key.StartsWith(article, StringComparison.Ordinal)
                && key.Length > article.Length)
            {
                return key[article.Length..];
            }
        }

        return key;
    }

    public static IReadOnlySet<string> CreatorTokens(string? creator)
    {
        if (string.IsNullOrWhiteSpace(creator))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return NormalizeForMatch(creator)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 2 && !x.All(char.IsDigit))
            .ToHashSet(StringComparer.Ordinal);
    }

    public static string CreatorKey(IReadOnlySet<string> tokens) =>
        string.Join(" ", tokens.OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Two token sets match when either is a subset of the other (both must be non-empty).</summary>
    public static bool TokensMatch(IReadOnlySet<string> left, IReadOnlySet<string> right) =>
        left.Count > 0
        && right.Count > 0
        && (left.IsSubsetOf(right) || right.IsSubsetOf(left));

    /// <summary>Alias for <see cref="TokensMatch"/> read at creator call sites.</summary>
    public static bool CreatorsMatch(IReadOnlySet<string> left, IReadOnlySet<string> right) =>
        TokensMatch(left, right);

    private static bool SubjectsMatch(IReadOnlySet<string> left, IReadOnlySet<string> right) =>
        TokensMatch(left, right);

    /// <summary>
    /// Same normalized main title and compatible creators. A missing creator on either side is treated
    /// as compatible so provider records without creator metadata cannot slip past suppression.
    /// </summary>
    public static bool SameEntity(RecommendationIdentity left, RecommendationIdentity right) =>
        left.TitleKey.Length > 0
        && left.TitleKey == right.TitleKey
        && (left.CreatorTokens.Count == 0
            || right.CreatorTokens.Count == 0
            || CreatorsMatch(left.CreatorTokens, right.CreatorTokens));

    private static bool IsOwned(
        string candidateId,
        RecommendationIdentity identity,
        IReadOnlyList<RecommendationOwnedEntity> owned) =>
        owned.Any(entity =>
            (!string.IsNullOrWhiteSpace(entity.CatalogId)
                && string.Equals(entity.CatalogId, candidateId, StringComparison.OrdinalIgnoreCase))
            || SameEntity(entity.Identity, identity));

    public static string NormalizeForMatch(string value) =>
        NonAlphanumeric()
            .Replace(value.ToLowerInvariant(), " ")
            .Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphanumeric();
}

/// <summary>
/// Candidates already placed on an earlier shelf, for cross-shelf and in-shelf duplicate suppression.
/// Matches by provider id first, then by normalized identity.
/// </summary>
public sealed class RecommendationUsedSet
{
    private readonly HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RecommendationIdentity> identities = [];

    public bool TryAdd(string id, RecommendationIdentity identity)
    {
        if (ids.Contains(id)
            || identities.Any(x => RecommendationScoring.SameEntity(x, identity)))
        {
            return false;
        }

        ids.Add(id);
        identities.Add(identity);
        return true;
    }
}
