using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Vocabulary;

public sealed record EpisodePreparationTerm(
    Guid TermId,
    string Canonical,
    string? Reading,
    string? Meaning,
    int EpisodeOccurrences,
    int AnimeOccurrences,
    UserTermState? State);

public sealed record EpisodePreparationSnapshot(
    int TargetPercent,
    int TotalOccurrences,
    int KnownOccurrences,
    int PreparedOccurrences,
    IReadOnlyList<EpisodePreparationTerm> Terms,
    IReadOnlyList<Guid> RecommendedTermIds)
{
    public static EpisodePreparationSnapshot Empty { get; } =
        new(95, 0, 0, 0, [], []);

    public int KnownPercent => Percentage(KnownOccurrences, TotalOccurrences);
    public int PreparedPercent => Percentage(PreparedOccurrences, TotalOccurrences);
    public int RecommendedCount => RecommendedTermIds.Count;

    private static int Percentage(int value, int total) =>
        total == 0 ? 0 : (int)Math.Floor((double)value / total * 100);
}

public static class EpisodePreparationPlanner
{
    public const double DefaultTargetCoverage = 0.95;

    public static EpisodePreparationSnapshot Build(
        IReadOnlyList<EpisodePreparationTerm> terms,
        double targetCoverage = DefaultTargetCoverage)
    {
        if (targetCoverage <= 0 || targetCoverage > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetCoverage));
        }

        if (terms.Count == 0)
        {
            return new EpisodePreparationSnapshot(
                (int)Math.Round(targetCoverage * 100),
                0,
                0,
                0,
                terms,
                []);
        }

        var totalOccurrences = terms.Sum(x => x.EpisodeOccurrences);
        var knownOccurrences = terms
            .Where(x => x.State == UserTermState.Known)
            .Sum(x => x.EpisodeOccurrences);
        var preparedOccurrences = terms
            .Where(x => x.State is UserTermState.Known or UserTermState.Learning)
            .Sum(x => x.EpisodeOccurrences);

        var targetOccurrences = (int)Math.Ceiling(totalOccurrences * targetCoverage);
        var selected = new List<Guid>();
        var coveredOccurrences = preparedOccurrences;

        foreach (var term in terms
                     .Where(x => x.State is null)
                     .OrderByDescending(x => x.EpisodeOccurrences)
                     .ThenByDescending(x => x.AnimeOccurrences)
                     .ThenBy(x => x.Canonical, StringComparer.Ordinal))
        {
            if (coveredOccurrences >= targetOccurrences)
            {
                break;
            }

            selected.Add(term.TermId);
            coveredOccurrences += term.EpisodeOccurrences;
        }

        return new EpisodePreparationSnapshot(
            (int)Math.Round(targetCoverage * 100),
            totalOccurrences,
            knownOccurrences,
            preparedOccurrences,
            terms,
            selected);
    }
}

public sealed class EpisodePreparationService(
    AppDbContext db,
    LearningService learningService)
{
    public async Task<EpisodePreparationSnapshot> GetAsync(
        Guid episodeId,
        Guid animeId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join term in db.Terms.AsNoTracking() on episodeTerm.TermId equals term.Id
            join userTermValue in db.UserTerms.AsNoTracking()
                    .Where(x => x.ProfileId == LearningProfile.DefaultId)
                on term.Id equals userTermValue.TermId into userTerms
            from userTerm in userTerms.DefaultIfEmpty()
            where episodeTerm.EpisodeId == episodeId
            orderby episodeTerm.Occurrences descending, term.Canonical
            select new
            {
                TermId = term.Id,
                term.Canonical,
                term.Reading,
                term.Meaning,
                EpisodeOccurrences = episodeTerm.Occurrences,
                State = userTerm == null ? (UserTermState?)null : userTerm.State
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return EpisodePreparationSnapshot.Empty;
        }

        var animeOccurrences = await (
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join episode in db.Episodes.AsNoTracking() on episodeTerm.EpisodeId equals episode.Id
            where episode.AnimeId == animeId
            group episodeTerm by episodeTerm.TermId into termGroup
            select new
            {
                TermId = termGroup.Key,
                Occurrences = termGroup.Sum(x => x.Occurrences)
            })
            .ToDictionaryAsync(x => x.TermId, x => x.Occurrences, cancellationToken);

        var terms = rows
            .Select(row => new EpisodePreparationTerm(
                row.TermId,
                row.Canonical,
                row.Reading,
                row.Meaning,
                row.EpisodeOccurrences,
                animeOccurrences.TryGetValue(row.TermId, out var count)
                    ? count
                    : row.EpisodeOccurrences,
                row.State))
            .ToArray();

        return EpisodePreparationPlanner.Build(terms);
    }

    public async Task<int?> PrepareToTargetAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var animeId = await db.Episodes
            .AsNoTracking()
            .Where(x => x.Id == episodeId)
            .Select(x => (Guid?)x.AnimeId)
            .SingleOrDefaultAsync(cancellationToken);

        if (animeId is null)
        {
            return null;
        }

        var snapshot = await GetAsync(episodeId, animeId.Value, cancellationToken);
        await learningService.AddToLearningAsync(snapshot.RecommendedTermIds, cancellationToken);
        return snapshot.RecommendedCount;
    }
}
