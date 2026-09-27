using Jularr.Web.Data;
using Jularr.Web.Features.Books;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Calendar;

/// <summary>
/// Web-novel chapters with a publication time from their source (for example Syosetu). A chapter
/// whose text is not fetched yet is missing locally.
/// </summary>
public sealed class NovelChapterReleaseEventSource(AppDbContext db) : IReleaseEventSource
{
    public string Name => "novel-chapters";

    public IReadOnlyCollection<ReleaseMediaType> MediaTypes { get; } = [ReleaseMediaType.LightNovel];

    public async Task<IReadOnlyList<ReleaseEvent>> GetEventsAsync(ReleaseEventQuery query, CancellationToken cancellationToken)
    {
        if (!query.Wants(ReleaseMediaType.LightNovel))
        {
            return [];
        }

        // Chapter times are UTC; one extra day on each side covers every viewer time zone.
        var from = query.Start.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = query.End.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await db.NovelChapters
            .AsNoTracking()
            .Where(chapter => chapter.PublishedAt != null && chapter.PublishedAt >= from && chapter.PublishedAt < to)
            .Join(
                db.NovelWorks.Where(work => work.SourceProvider != BookCatalogService.ImportedBookProvider),
                chapter => chapter.WorkId,
                work => work.Id,
                (chapter, work) => new
                {
                    chapter.Id,
                    chapter.WorkId,
                    chapter.Number,
                    chapter.PublishedAt,
                    HasContent = chapter.OriginalText != "",
                    Title = work.MetadataTitle ?? work.Title,
                    work.CoverImageUrl,
                    work.SourceProvider,
                    work.SourceKey
                })
            .Where(row => query.MediaId == null || row.WorkId == query.MediaId)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row =>
            {
                var unit = new ReleaseUnit(row.Number);
                var date = ReleaseDate.FromInstant(DateTime.SpecifyKind(row.PublishedAt!.Value, DateTimeKind.Utc));
                return new ReleaseEvent(
                    ReleaseEvent.BuildId(ReleaseMediaType.LightNovel, row.WorkId, ReleaseKind.Chapter, unit),
                    ReleaseMediaType.LightNovel,
                    row.WorkId,
                    row.Id,
                    ReleaseKind.Chapter,
                    row.Title,
                    unit,
                    date,
                    row.SourceProvider,
                    row.SourceKey,
                    new ReleaseLocalStatus(true, null, row.HasContent ? ReleaseLocalState.Available : ReleaseLocalState.Missing),
                    row.CoverImageUrl);
            })
            .Where(release => query.Includes(release.Date))
            .ToArray();
    }
}

/// <summary>
/// Publication dates of the primary edition of library books, with the precision the book
/// metadata has (often only a year). Books without a publication date produce no event.
/// </summary>
public sealed class BookReleaseEventSource(AppDbContext db) : IReleaseEventSource
{
    public string Name => "books";

    public IReadOnlyCollection<ReleaseMediaType> MediaTypes { get; } = [ReleaseMediaType.Book];

    public async Task<IReadOnlyList<ReleaseEvent>> GetEventsAsync(ReleaseEventQuery query, CancellationToken cancellationToken)
    {
        if (!query.Wants(ReleaseMediaType.Book))
        {
            return [];
        }

        var rows = await db.BookEditions
            .AsNoTracking()
            .Where(edition => edition.IsPrimary && edition.PublishedDate != null)
            .Join(
                db.NovelWorks.Where(work => work.SourceProvider == BookCatalogService.ImportedBookProvider),
                edition => edition.WorkId,
                work => work.Id,
                (edition, work) => new
                {
                    edition.Id,
                    edition.WorkId,
                    edition.PublishedDate,
                    edition.SourceProvider,
                    edition.SourceExternalId,
                    work.Title,
                    work.CoverImageUrl,
                    HasFile = db.BookFiles.Any(file => file.EditionId == edition.Id)
                })
            .Where(row => query.MediaId == null || row.WorkId == query.MediaId)
            .ToListAsync(cancellationToken);

        var events = new List<ReleaseEvent>();
        foreach (var row in rows)
        {
            if (!ReleaseDate.TryParse(row.PublishedDate, out var date) || !query.Includes(date))
            {
                continue;
            }

            events.Add(new ReleaseEvent(
                ReleaseEvent.BuildId(ReleaseMediaType.Book, row.WorkId, ReleaseKind.Publication, null, row.Id.ToString("N")),
                ReleaseMediaType.Book,
                row.WorkId,
                row.Id,
                ReleaseKind.Publication,
                row.Title,
                null,
                date,
                row.SourceProvider ?? BookCatalogService.ImportedBookProvider,
                row.SourceExternalId,
                new ReleaseLocalStatus(true, null, row.HasFile ? ReleaseLocalState.Available : ReleaseLocalState.None),
                row.CoverImageUrl));
        }

        return events;
    }
}
