using Jularr.Web.Data;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Mapping;
using Jularr.Web.Features.Novels;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.MediaCore;

/// <summary>
/// Non-invasive adapters that unify the existing per-type entities (Anime, NovelWork, BookEdition,
/// MangaSeries) under the universal <see cref="Work"/> model (#592). Each method ensures a work exists
/// for a legacy record and bridges it through a <see cref="WorkSourceLink"/> — the legacy tables are
/// never modified, so every current feature keeps working. Bridging is idempotent: the same legacy
/// record always resolves to the same work.
///
/// The per-type <b>population</b> from providers (rich metadata, full external-id sets, structure) is
/// deliberately minimal here and completed by the individual #556 library children; this class proves
/// the bridge and gives those children a stable entry point.
/// </summary>
public sealed class LegacyWorkBridge(AppDbContext db, WorkService works, WorkStructureService structure)
{
    /// <summary>Ensures the work for an anime record (bridges by <c>Anime.Id</c>).</summary>
    public async Task<Guid> EnsureWorkForAnimeAsync(Anime anime, CancellationToken cancellationToken)
    {
        var workId = await EnsureWorkAsync(
            WorkSourceKind.Anime, anime.Id, WorkMediaType.Anime, anime.Title, year: null, cancellationToken);

        await works.AddOrUpdateTitleAsync(
            workId, WorkTitleType.Primary, "und", anime.Title, MetadataFieldSources.Local, isPrimary: true, cancellationToken);
        // #435: record where the field came from so a later provider refresh respects the precedence
        // ladder (and never clobbers an owner correction). AniList is the built-in display-metadata role.
        await works.SetFieldProvenanceAsync(
            workId, "title", MetadataFieldSources.Local, null, null,
            isManualOverride: false, preferredProvider: MappingProviders.AniList, cancellationToken);
        return workId;
    }

    /// <summary>
    /// Ensures the work for a novel/light-novel record and mirrors its source and metadata provider
    /// identities plus its titles into the core. The caller decides whether the work is a
    /// <see cref="WorkMediaType.LightNovel"/> or a plain <see cref="WorkMediaType.Book"/>.
    /// </summary>
    public async Task<Guid> EnsureWorkForNovelAsync(
        NovelWork novel,
        WorkMediaType mediaType,
        CancellationToken cancellationToken)
    {
        var workId = await EnsureWorkAsync(
            WorkSourceKind.NovelWork, novel.Id, mediaType, novel.Title, year: null, cancellationToken);

        await works.AddOrUpdateTitleAsync(
            workId, WorkTitleType.Primary, "und", novel.Title, novel.SourceProvider, isPrimary: true, cancellationToken);
        await works.SetFieldProvenanceAsync(
            workId, "title", novel.SourceProvider, novel.SourceKey, null,
            isManualOverride: false, preferredProvider: novel.MetadataProvider ?? novel.SourceProvider, cancellationToken);

        if (!string.IsNullOrWhiteSpace(novel.MetadataNativeTitle))
        {
            await works.AddOrUpdateTitleAsync(
                workId, WorkTitleType.Native, "und", novel.MetadataNativeTitle!,
                novel.MetadataProvider ?? "", isPrimary: false, cancellationToken);
            await works.SetFieldProvenanceAsync(
                workId, "originalTitle", novel.MetadataProvider ?? "", novel.MetadataExternalId, null,
                isManualOverride: false, preferredProvider: novel.MetadataProvider ?? "", cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(novel.SourceProvider) && !string.IsNullOrWhiteSpace(novel.SourceKey))
        {
            await works.LinkExternalIdentityAsync(
                workId, mediaType, novel.SourceProvider, novel.SourceKey,
                confidence: 1.0, evidence: "novel source key", isPrimary: true,
                isManualOverride: false, MappingReviewState.Confirmed, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(novel.MetadataProvider) && !string.IsNullOrWhiteSpace(novel.MetadataExternalId))
        {
            await works.LinkExternalIdentityAsync(
                workId, mediaType, novel.MetadataProvider!, novel.MetadataExternalId!,
                confidence: 1.0, evidence: "novel metadata id", isPrimary: false,
                isManualOverride: false, MappingReviewState.Confirmed, cancellationToken);
        }

        return workId;
    }

    /// <summary>
    /// Ensures the work for a book by bridging its parent novel work, and mirrors the book edition into
    /// a <see cref="WorkEdition"/> (editions are modelled separately from versions, #592).
    /// </summary>
    public async Task<Guid> EnsureWorkForBookEditionAsync(BookEdition edition, CancellationToken cancellationToken)
    {
        var novel = await db.Set<NovelWork>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == edition.WorkId, cancellationToken)
            ?? throw new InvalidOperationException($"Book edition {edition.Id:D} has no parent novel work.");

        var workId = await EnsureWorkForNovelAsync(novel, WorkMediaType.Book, cancellationToken);

        await structure.AddOrUpdateEditionAsync(
            workId,
            editionKey: edition.EditionKey,
            language: edition.Language,
            format: null,
            publisher: edition.Publisher,
            isbn13: edition.Isbn13,
            title: edition.Title,
            isPrimary: edition.IsPrimary,
            cancellationToken);

        // Bridge the edition record itself so it resolves to the same work.
        await works.LinkSourceAsync(workId, WorkSourceKind.BookEdition, edition.Id, cancellationToken);

        if (!string.IsNullOrWhiteSpace(edition.Isbn13))
        {
            await works.LinkExternalIdentityAsync(
                workId, WorkMediaType.Book, "isbn", edition.Isbn13!,
                confidence: 1.0, evidence: "book ISBN-13", isPrimary: false,
                isManualOverride: false, MappingReviewState.Confirmed, cancellationToken);
        }

        return workId;
    }

    /// <summary>
    /// Ensures the work for a manga series. MangaSeries is a raw-SQL table (not an EF entity), so the
    /// caller passes the fields; the bridge is keyed by the series id.
    /// </summary>
    public async Task<Guid> EnsureWorkForMangaSeriesAsync(
        Guid seriesId,
        string title,
        string? nativeTitle,
        string? aniListId,
        CancellationToken cancellationToken)
    {
        var workId = await EnsureWorkAsync(
            WorkSourceKind.MangaSeries, seriesId, WorkMediaType.Manga, title, year: null, cancellationToken);

        await works.AddOrUpdateTitleAsync(
            workId, WorkTitleType.Primary, "und", title, MetadataFieldSources.Local, isPrimary: true, cancellationToken);
        await works.SetFieldProvenanceAsync(
            workId, "title", MetadataFieldSources.Local, null, null,
            isManualOverride: false, preferredProvider: MappingProviders.AniList, cancellationToken);

        if (!string.IsNullOrWhiteSpace(nativeTitle))
        {
            await works.AddOrUpdateTitleAsync(
                workId, WorkTitleType.Native, "und", nativeTitle!, MappingProviders.AniList, isPrimary: false, cancellationToken);
            await works.SetFieldProvenanceAsync(
                workId, "originalTitle", MappingProviders.AniList, aniListId, null,
                isManualOverride: false, preferredProvider: MappingProviders.AniList, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(aniListId))
        {
            await works.LinkExternalIdentityAsync(
                workId, WorkMediaType.Manga, MappingProviders.AniList, aniListId!,
                confidence: 1.0, evidence: "manga AniList id", isPrimary: true,
                isManualOverride: false, MappingReviewState.Confirmed, cancellationToken);
        }

        return workId;
    }

    /// <summary>Resolves the existing work bridged to a legacy record, creating and linking one if absent.</summary>
    private async Task<Guid> EnsureWorkAsync(
        WorkSourceKind sourceKind,
        Guid sourceId,
        WorkMediaType mediaType,
        string title,
        int? year,
        CancellationToken cancellationToken)
    {
        var existing = await db.Set<WorkSourceLink>().AsNoTracking()
            .Where(x => x.SourceKind == sourceKind && x.SourceId == sourceId)
            .Select(x => (Guid?)x.WorkId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is { } workId)
        {
            return workId;
        }

        var work = await works.CreateWorkAsync(mediaType, title, year, cancellationToken);
        await works.LinkSourceAsync(work.Id, sourceKind, sourceId, cancellationToken);
        return work.Id;
    }
}
