using AniLingo.Web.Features.Operations;

namespace AniLingo.Web.Features.Books;

/// <summary>
/// The one operation that imports completed EPUB files from the Books inbox.
/// It runs when the owner requests it or when a Books SABnzbd download
/// completes, never as a side effect of opening a page.
/// </summary>
public static class BookInboxImport
{
    public const string OperationKind = "book-inbox-import";
    public const string SabnzbdDownloadKind = "sabnzbd-download";

    public static Task<IReadOnlyList<Guid>> RunAsync(
        OperationRunner operations,
        BookCatalogService books,
        string? profileId,
        CancellationToken cancellationToken) =>
        operations.RunAsync(
            new OperationDescriptor(
                OperationKind,
                "Books",
                "Import Books inbox",
                ProfileId: profileId,
                Lane: OperationLane.Normal,
                Retryable: false),
            async (operation, token) =>
            {
                await operation.ReportAsync(
                    10,
                    "Scanning Books inbox.",
                    cancellationToken: token);

                return await books.ImportInboxAsync(token);
            },
            "Books inbox scan completed.",
            cancellationToken);

    /// <summary>
    /// Imports the inbox once when at least one completed operation was a
    /// Books SABnzbd download. Returns whether an import ran.
    /// </summary>
    public static async Task<bool> ImportAfterDownloadsAsync(
        IServiceProvider services,
        IReadOnlyList<OperationSnapshot> completed,
        CancellationToken cancellationToken)
    {
        var trigger = completed.FirstOrDefault(IsBookDownload);
        if (trigger is null)
        {
            return false;
        }

        var books = services.GetRequiredService<BookCatalogService>();
        if (!books.IsInboxConfigured)
        {
            return false;
        }

        // One scan imports every EPUB in the inbox, so several downloads
        // finishing in the same poll need only one import.
        await RunAsync(
            services.GetRequiredService<OperationRunner>(),
            books,
            trigger.ProfileId,
            cancellationToken);

        return true;
    }

    public static bool IsBookDownload(OperationSnapshot operation) =>
        string.Equals(
            operation.Kind,
            SabnzbdDownloadKind,
            StringComparison.Ordinal);
}
