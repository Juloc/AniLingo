using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Operations;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Books;

/// <summary>
/// What the Add book dialog shows for one catalog book: the library work when the book is
/// already there, otherwise the latest request with the state the user should see.
/// </summary>
public sealed record BookAddState(
    Guid? LibraryWorkId,
    string? RequestStatus,
    string? RequestMessage,
    int? ProgressPercent,
    string? RequestCatalogId = null)
{
    /// <summary>Visible request states. <c>importing</c> is a finished download whose import has not closed the request yet.</summary>
    public const string Importing = "importing";

    public static readonly BookAddState None = new(null, null, null, null);

    /// <summary>Still moving on its own, so the dialog keeps refreshing it.</summary>
    public bool IsInFlight =>
        LibraryWorkId is null
        && RequestStatus is "pending" or "approved" or "searching" or "downloading" or Importing;
}

/// <summary>
/// One catalog book to look up: its id, its title (for library matching) and the ids of the
/// same work at other providers, which may carry the library link or the request.
/// </summary>
public sealed record BookAddLookup(string CatalogId, string? Title = null, IReadOnlyList<string>? Aliases = null)
{
    public IEnumerable<string> Ids => Aliases is null ? [CatalogId] : [CatalogId, .. Aliases.Where(alias => alias != CatalogId)];
}

/// <summary>
/// Reads the canonical library and request state for catalog books, so a search result and
/// the dialog's refresh always agree with the Books library: a book imported for a request is
/// found by the catalog id its request linked (at any provider of the work), other books by
/// their normalized title.
/// </summary>
public sealed class BookAddStateQuery(AppDbContext db, AcquisitionAccessStore requests)
{
    public async Task<IReadOnlyDictionary<string, BookAddState>> GetAsync(
        IReadOnlyList<BookAddLookup> books,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, BookAddState>(StringComparer.Ordinal);
        if (books.Count == 0)
        {
            return result;
        }

        var works = await db.NovelWorks
            .AsNoTracking()
            .Where(work => work.SourceProvider == BookCatalogService.ImportedBookProvider)
            .Select(work => new { work.Id, Title = work.MetadataTitle ?? work.Title, work.MetadataExternalId })
            .ToListAsync(cancellationToken);
        var byCatalogId = works
            .Where(work => work.MetadataExternalId != null)
            .GroupBy(work => work.MetadataExternalId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);
        var byTitle = works
            .GroupBy(work => NormalizeTitle(work.Title))
            .ToDictionary(group => group.Key, group => group.First().Id);
        var byMainTitle = works
            .GroupBy(work => NormalizeTitle(BookWorkSearch.MainTitle(work.Title)))
            .ToDictionary(group => group.Key, group => group.First().Id);
        var workIds = works.Select(work => work.Id).ToHashSet();
        Guid? LibraryByTitle(string? title) =>
            title is null ? null
            : byTitle.TryGetValue(NormalizeTitle(title), out var named) ? named
            : byMainTitle.TryGetValue(NormalizeTitle(BookWorkSearch.MainTitle(title)), out var mainNamed) ? mainNamed
            : null;

        var catalogIds = books.SelectMany(book => book.Ids).ToHashSet(StringComparer.Ordinal);
        var latest = (await requests.ListAsync(MediaAcquisitionKind.Book, null, openOnly: false, limit: 500, cancellationToken))
            .Where(request => request.Provider == BookCatalogService.CatalogRequestProvider && catalogIds.Contains(request.ExternalId))
            .GroupBy(request => request.ExternalId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(request => request.UpdatedAt).First(), StringComparer.Ordinal);
        var operations = new OperationStore(db);

        foreach (var book in books)
        {
            // The newest request for any identity of this work is its request.
            var request = book.Ids
                .Select(id => latest.GetValueOrDefault(id))
                .OfType<AcquisitionRequest>()
                .OrderByDescending(candidate => candidate.IsOpen)
                .ThenByDescending(candidate => candidate.UpdatedAt)
                .FirstOrDefault();
            Guid? workId = book.Ids.Select(id => byCatalogId.TryGetValue(id, out var linked) ? linked : (Guid?)null).FirstOrDefault(id => id is not null)
                ?? (ResultWorkId(request) is { } resulting && workIds.Contains(resulting) ? resulting : (Guid?)null)
                ?? LibraryByTitle(book.Title);
            result[book.CatalogId] = workId is not null
                ? new BookAddState(workId, null, null, null)
                : (await RequestStateAsync(request, operations, cancellationToken)) with { RequestCatalogId = request?.ExternalId };
        }

        return result;
    }

    private static async Task<BookAddState> RequestStateAsync(
        AcquisitionRequest? request,
        OperationStore operations,
        CancellationToken cancellationToken)
    {
        // A finished or withdrawn request whose book is not in the library any more is no state
        // worth showing: the book can simply be added again. Failures stay visible with their reason.
        if (request is null || request.Status is AcquisitionRequestStatus.Completed or AcquisitionRequestStatus.Rejected)
        {
            return BookAddState.None;
        }

        if (request.Status == AcquisitionRequestStatus.Downloading && request.OperationId is { } operationId)
        {
            var operation = await operations.GetAsync(operationId, cancellationToken);
            return operation?.Status == OperationStatus.Succeeded
                ? new BookAddState(null, BookAddState.Importing, request.StatusMessage, 100)
                : new BookAddState(null, "downloading", request.StatusMessage, operation?.ProgressPercent);
        }

        // Approved and waiting for the next search is still searching from the user's view.
        var status = request.Status == AcquisitionRequestStatus.Approved
            && BookAcquisitionExecutor.ReadPayload(request).NextSearchUtc is not null
                ? "searching"
                : AcquisitionAccessNames.Status(request.Status);
        return new BookAddState(null, status, request.StatusMessage, null);
    }

    private static Guid? ResultWorkId(AcquisitionRequest? request)
    {
        const string prefix = "/Books/Library/";
        return request?.ResultUrl is { } url
            && url.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParse(url[prefix.Length..], out var id)
                ? id
                : null;
    }

    public static string NormalizeTitle(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
