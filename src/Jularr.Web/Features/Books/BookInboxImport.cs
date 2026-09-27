using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Operations;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Books;

/// <summary>
/// The one operation that imports completed EPUB files from the Books inbox.
/// It runs when the owner requests it or when a Books SABnzbd download
/// completes, never as a side effect of opening a page.
/// </summary>
public static class BookInboxImport
{
    public const string OperationKind = "book-inbox-import";
    public const string SabnzbdDownloadKind = "sabnzbd-download";
    public const string DownloadImportOperationKind = "book-download-import";

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
    /// Imports completed Books SABnzbd downloads. Each job's own storage folder (as SABnzbd reports
    /// it, translated with the canonical remote path mappings) is imported directly, including
    /// subfolders; the Books inbox is only a fallback for jobs without a readable storage path.
    /// Returns whether anything was imported.
    /// </summary>
    public static async Task<bool> ImportAfterDownloadsAsync(
        IServiceProvider services,
        IReadOnlyList<OperationSnapshot> completed,
        IReadOnlyDictionary<Guid, string?> storagePaths,
        CancellationToken cancellationToken)
    {
        var downloads = completed.Where(IsBookDownload).ToArray();
        if (downloads.Length == 0)
        {
            return false;
        }

        var books = services.GetRequiredService<BookCatalogService>();
        var operations = services.GetRequiredService<OperationRunner>();
        var mappings = await services.GetRequiredService<AnimeImportSettingsStore>().LoadAsync(cancellationToken);
        var needsInbox = new List<OperationSnapshot>();
        var importedAny = false;

        foreach (var download in downloads)
        {
            var remote = storagePaths.GetValueOrDefault(download.Id);
            var local = string.IsNullOrWhiteSpace(remote) ? null : mappings.TranslatePath(remote);
            if (local is null || !(Directory.Exists(local) || File.Exists(local)))
            {
                needsInbox.Add(download);
                continue;
            }

            var imported = await operations.RunAsync(
                new OperationDescriptor(
                    DownloadImportOperationKind,
                    "Books",
                    "Import downloaded book",
                    local,
                    download.ProfileId,
                    OperationLane.Normal,
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(10, "Importing EPUB files from the completed download.", cancellationToken: token);
                    return await books.ImportEpubsFromPathAsync(local, "download", token);
                },
                "Downloaded book imported.",
                cancellationToken);

            importedAny |= imported.Count > 0;
            await CompleteBookRequestsAsync(services, [download], imported, cancellationToken);
        }

        if (needsInbox.Count > 0 && books.IsInboxConfigured)
        {
            // One inbox scan imports every EPUB there, so several jobs need only one scan.
            var imported = await RunAsync(operations, books, needsInbox[0].ProfileId, cancellationToken);
            importedAny |= imported.Count > 0;
            await CompleteBookRequestsAsync(services, needsInbox, imported, cancellationToken);
        }
        else if (needsInbox.Count > 0)
        {
            await CompleteBookRequestsAsync(services, needsInbox, [], cancellationToken);
        }

        return importedAny;
    }

    /// <summary>
    /// Closes the Books acquisition requests whose SABnzbd download just finished, linking each to
    /// the imported work with the matching title (or the only import of this scan).
    /// </summary>
    public static async Task CompleteBookRequestsAsync(
        IServiceProvider services,
        IReadOnlyList<OperationSnapshot> completed,
        IReadOnlyList<Guid> importedWorkIds,
        CancellationToken cancellationToken)
    {
        var finished = completed.Where(IsBookDownload).Select(operation => operation.Id).ToHashSet();
        if (finished.Count == 0)
        {
            return;
        }

        var store = services.GetRequiredService<AcquisitionAccessStore>();
        var waiting = (await store.ListDownloadingAsync(MediaAcquisitionKind.Book, cancellationToken))
            .Where(request => request.OperationId is { } operationId && finished.Contains(operationId))
            .ToArray();
        if (waiting.Length == 0)
        {
            return;
        }

        var db = services.GetRequiredService<AppDbContext>();
        var imported = await db.NovelWorks
            .AsNoTracking()
            .Where(work => importedWorkIds.Contains(work.Id))
            .Select(work => new { work.Id, Title = work.MetadataTitle ?? work.Title })
            .ToListAsync(cancellationToken);

        foreach (var request in waiting)
        {
            var match = imported.FirstOrDefault(work => SameTitle(work.Title, request.Title))
                ?? (imported.Count == 1 || waiting.Length == 1 ? imported.FirstOrDefault() : null);
            await store.UpdateStatusAsync(
                request.Id,
                match is null ? AcquisitionRequestStatus.Failed : AcquisitionRequestStatus.Completed,
                match is null ? "The download finished but no matching EPUB was imported." : null,
                null,
                match is null ? null : $"/Books/Library/{match.Id}",
                null,
                cancellationToken);
        }
    }

    private static bool SameTitle(string imported, string requested)
    {
        static string Normalize(string value) =>
            new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        var left = Normalize(imported);
        var right = Normalize(requested);
        return left.Length > 0 && right.Length > 0 && (left.Contains(right) || right.Contains(left));
    }

    public static bool IsBookDownload(OperationSnapshot operation) =>
        string.Equals(
            operation.Kind,
            SabnzbdDownloadKind,
            StringComparison.Ordinal);
}
