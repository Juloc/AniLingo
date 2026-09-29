using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.MediaCore;

/// <summary>One resolved external identity, flattened for the query surface.</summary>
public sealed record WorkExternalIdentityView(
    WorkMediaType MediaType,
    string Provider,
    string ExternalId,
    bool IsPrimary,
    double Confidence,
    bool IsManualOverride,
    MappingReviewState ReviewState);

/// <summary>A directed relation edge from the queried work to another work.</summary>
public sealed record WorkRelationView(
    Guid FromWorkId,
    Guid ToWorkId,
    WorkRelationType RelationType,
    string Source,
    bool IsManualOverride);

/// <summary>
/// Aggregate read model of a work for the other #556 children (Movie/TV libraries, discovery,
/// instant-play, requests, shell) to build on: the stable id, media type, resolved titles, all
/// external identities, typed relations and bridged legacy sources.
/// </summary>
public sealed record WorkSummary(
    Guid Id,
    WorkMediaType MediaType,
    string CanonicalTitle,
    int? Year,
    IReadOnlyList<WorkTitle> Titles,
    IReadOnlyList<WorkExternalIdentityView> Identities,
    IReadOnlyList<WorkRelationView> Relations,
    IReadOnlyList<WorkSourceLink> Sources);

/// <summary>
/// The read surface of the universal media core (#592). All reads are <c>AsNoTracking</c> projections;
/// nothing loads a whole library into memory. Identity resolution is the entry point discovery and
/// instant-play use to unify and de-duplicate provider results over one stable Jularr id.
/// </summary>
public sealed class WorkQueryService(AppDbContext db)
{
    /// <summary>Resolves the stable Jularr work id a provider identity points at, or null.</summary>
    public async Task<Guid?> FindWorkIdByExternalIdentityAsync(
        WorkMediaType mediaType,
        string provider,
        string externalId,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = MediaCoreNormalization.NormalizeProvider(provider);
        var normalizedExternalId = MediaCoreNormalization.NormalizeExternalId(externalId);

        var id = await db.Set<WorkExternalIdentity>()
            .AsNoTracking()
            .Where(x => x.MediaType == mediaType
                && x.Provider == normalizedProvider
                && x.ExternalId == normalizedExternalId)
            .Select(x => (Guid?)x.WorkId)
            .FirstOrDefaultAsync(cancellationToken);

        return id;
    }

    public Task<Work?> GetWorkAsync(Guid workId, CancellationToken cancellationToken) =>
        db.Set<Work>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == workId, cancellationToken);

    public async Task<IReadOnlyList<WorkTitle>> GetTitlesAsync(Guid workId, CancellationToken cancellationToken) =>
        await db.Set<WorkTitle>().AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.TitleType)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkExternalIdentityView>> GetIdentitiesAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        await db.Set<WorkExternalIdentity>().AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Provider)
            .Select(x => new WorkExternalIdentityView(
                x.MediaType, x.Provider, x.ExternalId, x.IsPrimary, x.Confidence, x.IsManualOverride, x.ReviewState))
            .ToListAsync(cancellationToken);

    /// <summary>All relation edges touching the work, in both directions.</summary>
    public async Task<IReadOnlyList<WorkRelationView>> GetRelationsAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        await db.Set<WorkRelation>().AsNoTracking()
            .Where(x => x.FromWorkId == workId || x.ToWorkId == workId)
            .Select(x => new WorkRelationView(
                x.FromWorkId, x.ToWorkId, x.RelationType, x.Source, x.IsManualOverride))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkSeason>> GetSeasonsAsync(Guid workId, CancellationToken cancellationToken) =>
        await db.Set<WorkSeason>().AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.SeasonNumber)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkEpisode>> GetEpisodesAsync(Guid workId, CancellationToken cancellationToken) =>
        await db.Set<WorkEpisode>().AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.SeasonNumber)
            .ThenBy(x => x.EpisodeNumber)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkVolume>> GetVolumesAsync(Guid workId, CancellationToken cancellationToken) =>
        await db.Set<WorkVolume>().AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.Number)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkChapter>> GetChaptersAsync(Guid workId, CancellationToken cancellationToken) =>
        await db.Set<WorkChapter>().AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.Number)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkEdition>> GetEditionsAsync(Guid workId, CancellationToken cancellationToken) =>
        await db.Set<WorkEdition>().AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.EditionKey)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkVersion>> GetVersionsAsync(Guid workId, CancellationToken cancellationToken) =>
        await db.Set<WorkVersion>().AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.UnitKey)
            .ThenBy(x => x.VersionKey)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkFieldProvenance>> GetProvenanceAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        await db.Set<WorkFieldProvenance>().AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.FieldKey)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkSourceLink>> GetSourceLinksAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        await db.Set<WorkSourceLink>().AsNoTracking()
            .Where(x => x.WorkId == workId)
            .ToListAsync(cancellationToken);

    /// <summary>Resolves the Jularr work bridged to a given legacy per-type record, or null.</summary>
    public async Task<Guid?> ResolveWorkForSourceAsync(
        WorkSourceKind sourceKind,
        Guid sourceId,
        CancellationToken cancellationToken) =>
        await db.Set<WorkSourceLink>().AsNoTracking()
            .Where(x => x.SourceKind == sourceKind && x.SourceId == sourceId)
            .Select(x => (Guid?)x.WorkId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The aggregate read model other #556 children consume.</summary>
    public async Task<WorkSummary?> GetSummaryAsync(Guid workId, CancellationToken cancellationToken)
    {
        var work = await GetWorkAsync(workId, cancellationToken);
        if (work is null)
        {
            return null;
        }

        return new WorkSummary(
            work.Id,
            work.MediaType,
            work.CanonicalTitle,
            work.Year,
            await GetTitlesAsync(workId, cancellationToken),
            await GetIdentitiesAsync(workId, cancellationToken),
            await GetRelationsAsync(workId, cancellationToken),
            await GetSourceLinksAsync(workId, cancellationToken));
    }
}
