using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Health;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Admin;

public enum UsenetCheckState
{
    Ok,
    Warning,
    Missing,
    Unknown
}

/// <summary>One step of the Usenet setup checklist.</summary>
public sealed record UsenetCheck(string Key, UsenetCheckState State, string Detail, string? LinkPage);

public sealed record UsenetIndexerCard(IndexerEntry Entry, AcquisitionHealthStatus? Health);

public sealed record UsenetClientCard(DownloadClientEntry Entry, AcquisitionHealthStatus? Health);

/// <summary>One SABnzbd job for the recent downloads list; <see cref="LocalPathReadable"/> is null when unknown.</summary>
public sealed record UsenetDownloadRow(
    string Name,
    string? Category,
    string Status,
    double? Percentage,
    long? SizeBytes,
    DateTimeOffset? CompletedAt,
    string? StoragePath,
    string? LocalPath,
    bool? LocalPathReadable,
    string? FailureMessage,
    bool IsActive);

/// <summary>
/// The owner's Usenet hub: a setup checklist, every indexer and SABnzbd connection with health,
/// test, enable, edit and remove, a book search test that shows which release automatic adding
/// would pick, and the recent SABnzbd queue and history with whether Jularr can read each result.
/// </summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class UsenetModel(
    AppDbContext db,
    IndexerStore indexerStore,
    IReadOnlyDictionary<IndexerType, IIndexer> indexers,
    IndexerSearchCoordinator searchCoordinator,
    DownloadClientStore clientStore,
    IDownloadClient downloadClient,
    ISabnzbdClient sabnzbd,
    AcquisitionHealthStore health,
    AnimeImportSettingsStore importSettings,
    AcquisitionAccessStore access,
    BookCatalogService books) : PageModel
{
    private static readonly TimeSpan SabnzbdTimeout = TimeSpan.FromSeconds(6);

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<UsenetCheck> Checks { get; private set; } = [];
    public IReadOnlyList<UsenetIndexerCard> Indexers { get; private set; } = [];
    public IReadOnlyList<UsenetClientCard> Clients { get; private set; } = [];
    public IReadOnlyList<UsenetDownloadRow> Downloads { get; private set; } = [];
    public string? DownloadsError { get; private set; }
    public string? DownloadsClientName { get; private set; }
    public int RemotePathMappingCount { get; private set; }
    public string? BooksInboxPath { get; private set; }
    public AcquisitionAccessPolicy? BookPolicy { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? TestTitle { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TestAuthor { get; set; }

    public BookUsenetSearchResult? TestResult { get; private set; }

    public string? Notice => TempData["UsenetNotice"] as string;
    public string? Error => TempData["UsenetError"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var indexerEntries = (await indexerStore.LoadAllAsync(cancellationToken))
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var indexerCards = new List<UsenetIndexerCard>();
        foreach (var entry in indexerEntries)
        {
            indexerCards.Add(new UsenetIndexerCard(entry, await health.GetAsync(AcquisitionHealthKind.Indexer, entry.Id, cancellationToken)));
        }

        Indexers = indexerCards;

        var clientEntries = (await clientStore.LoadAllAsync(cancellationToken))
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var clientCards = new List<UsenetClientCard>();
        foreach (var entry in clientEntries)
        {
            clientCards.Add(new UsenetClientCard(entry, await health.GetAsync(AcquisitionHealthKind.DownloadClient, entry.Id, cancellationToken)));
        }

        Clients = clientCards;

        var mappings = await importSettings.LoadAsync(cancellationToken);
        RemotePathMappingCount = mappings.RemotePathMappings.Count;
        BooksInboxPath = books.InboxPath;
        BookPolicy = await access.GetPolicyAsync(MediaAcquisitionKind.Book, cancellationToken);

        var activeClient = clientCards.FirstOrDefault(card => card.Entry.Enabled && card.Health?.IsHealthy != false)
            ?? clientCards.FirstOrDefault(card => card.Entry.Enabled);
        if (activeClient is not null)
        {
            await LoadDownloadsAsync(activeClient.Entry, mappings, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(TestTitle))
        {
            TestResult = await BookUsenetSearch.SearchAsync(searchCoordinator, TestTitle, TestAuthor, cancellationToken);
        }

        Checks = BuildChecks();
    }

    public async Task<IActionResult> OnPostTestIndexerAsync(Guid id, CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var entry = await indexerStore.GetAsync(id, cancellationToken);
        if (entry is null || !indexers.TryGetValue(entry.Type, out var indexer))
        {
            TempData["UsenetError"] = ui["settings.indexers.notFound"];
            return RedirectToPage();
        }

        var result = await TestIndexerAsync(entry, indexer, cancellationToken);
        TempData[result.Success ? "UsenetNotice" : "UsenetError"] = result.Success
            ? (result.Version is null
                ? ui.Format("settings.indexers.connected", ("name", entry.Name))
                : ui.Format("settings.indexers.connectedWithVersion", ("name", entry.Name), ("version", result.Version)))
            : result.Error ?? ui["settings.indexers.connectionFailed"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleIndexerAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await indexerStore.GetAsync(id, cancellationToken);
        if (entry is not null)
        {
            await indexerStore.SaveAsync(entry with { Enabled = !entry.Enabled }, cancellationToken);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteIndexerAsync(Guid id, CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await indexerStore.DeleteAsync(id, cancellationToken);
        await health.RemoveAsync(AcquisitionHealthKind.Indexer, id, cancellationToken);
        TempData["UsenetNotice"] = ui["settings.indexers.removed"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestClientAsync(Guid id, CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var entry = await clientStore.GetAsync(id, cancellationToken);
        if (entry is null)
        {
            TempData["UsenetError"] = ui["settings.downloadClients.notFound"];
            return RedirectToPage();
        }

        var result = await TestClientAsync(entry, cancellationToken);
        TempData[result.Success ? "UsenetNotice" : "UsenetError"] = result.Success
            ? (result.Version is null
                ? ui.Format("settings.downloadClients.connected", ("name", entry.Name))
                : ui.Format("settings.downloadClients.connectedWithVersion", ("name", entry.Name), ("version", result.Version)))
            : result.Error ?? ui["settings.downloadClients.connectionFailed"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleClientAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await clientStore.GetAsync(id, cancellationToken);
        if (entry is not null)
        {
            await clientStore.SaveAsync(entry with { Enabled = !entry.Enabled }, cancellationToken);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteClientAsync(Guid id, CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await clientStore.DeleteAsync(id, cancellationToken);
        await health.RemoveAsync(AcquisitionHealthKind.DownloadClient, id, cancellationToken);
        TempData["UsenetNotice"] = ui["settings.downloadClients.removed"];
        return RedirectToPage();
    }

    /// <summary>Tests every enabled indexer and download client in one go.</summary>
    public async Task<IActionResult> OnPostTestAllAsync(CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var failures = new List<string>();
        var tested = 0;
        foreach (var entry in (await indexerStore.LoadAllAsync(cancellationToken)).Where(entry => entry.Enabled))
        {
            if (!indexers.TryGetValue(entry.Type, out var indexer))
            {
                continue;
            }

            tested++;
            var result = await TestIndexerAsync(entry, indexer, cancellationToken);
            if (!result.Success)
            {
                failures.Add($"{entry.Name}: {result.Error ?? ui["settings.indexers.connectionFailed"]}");
            }
        }

        foreach (var entry in (await clientStore.LoadAllAsync(cancellationToken)).Where(entry => entry.Enabled))
        {
            tested++;
            var result = await TestClientAsync(entry, cancellationToken);
            if (!result.Success)
            {
                failures.Add($"{entry.Name}: {result.Error ?? ui["settings.downloadClients.connectionFailed"]}");
            }
        }

        if (failures.Count == 0)
        {
            TempData["UsenetNotice"] = ui.Format("admin.usenet.testAllOk", ("count", tested));
        }
        else
        {
            TempData["UsenetError"] = string.Join(" · ", failures);
        }

        return RedirectToPage();
    }

    private async Task<IndexerConnectionTestResult> TestIndexerAsync(IndexerEntry entry, IIndexer indexer, CancellationToken cancellationToken)
    {
        var result = await indexer.TestAsync(entry, cancellationToken);
        await health.RecordAsync(
            new AcquisitionHealthStatus(
                AcquisitionHealthKind.Indexer, entry.Id, entry.Name, true, result.Success, result.Error, DateTimeOffset.UtcNow),
            cancellationToken);
        return result;
    }

    private async Task<DownloadClientTestResult> TestClientAsync(DownloadClientEntry entry, CancellationToken cancellationToken)
    {
        var result = await downloadClient.TestAsync(entry, cancellationToken);
        await health.RecordAsync(
            new AcquisitionHealthStatus(
                AcquisitionHealthKind.DownloadClient, entry.Id, entry.Name, true, result.Success, result.Error, DateTimeOffset.UtcNow),
            cancellationToken);
        return result;
    }

    private async Task LoadDownloadsAsync(DownloadClientEntry entry, AnimeImportSettingsState mappings, CancellationToken cancellationToken)
    {
        DownloadsClientName = entry.Name;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SabnzbdTimeout);
        try
        {
            var connection = SabnzbdDownloadClient.ToConnection(entry);
            var queue = await sabnzbd.GetQueueAsync(connection, timeout.Token);
            var history = await sabnzbd.GetHistoryAsync(connection, null, timeout.Token);
            var rows = new List<UsenetDownloadRow>();
            rows.AddRange(queue.Jobs.Take(10).Select(job => new UsenetDownloadRow(
                job.Name, job.Category, job.Status ?? "Queued", job.Percentage, job.SizeBytes, null, null, null, null, null, IsActive: true)));
            foreach (var job in history.Jobs.Take(15))
            {
                string? local = null;
                bool? readable = null;
                if (job.IsCompleted && !string.IsNullOrWhiteSpace(job.StoragePath))
                {
                    local = mappings.TranslatePath(job.StoragePath);
                    readable = Directory.Exists(local) || System.IO.File.Exists(local);
                }

                rows.Add(new UsenetDownloadRow(
                    job.Name,
                    job.Category,
                    job.Status ?? (job.IsFailed ? "Failed" : "Completed"),
                    null,
                    job.SizeBytes,
                    job.CompletedAt,
                    job.StoragePath,
                    local,
                    readable,
                    job.FailureMessage,
                    IsActive: false));
            }

            Downloads = rows;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException
            && !cancellationToken.IsCancellationRequested)
        {
            DownloadsError = exception is TaskCanceledException ? Ui["admin.usenet.downloadsTimeout"] : exception.Message;
        }
    }

    private List<UsenetCheck> BuildChecks()
    {
        var checks = new List<UsenetCheck>();

        var enabledIndexers = Indexers.Where(card => card.Entry.Enabled).ToArray();
        checks.Add(enabledIndexers.Length == 0
            ? new UsenetCheck("indexer", UsenetCheckState.Missing, Ui["admin.usenet.check.indexer.missing"], "/Settings/Indexers/Edit")
            : enabledIndexers.Any(card => card.Health?.IsHealthy == true)
                ? new UsenetCheck("indexer", UsenetCheckState.Ok, string.Join(", ", enabledIndexers.Where(card => card.Health?.IsHealthy == true).Select(card => card.Entry.Name)), null)
                : new UsenetCheck("indexer", UsenetCheckState.Warning, Ui["admin.usenet.check.indexer.untested"], null));

        var enabledClients = Clients.Where(card => card.Entry.Enabled).ToArray();
        checks.Add(enabledClients.Length == 0
            ? new UsenetCheck("client", UsenetCheckState.Missing, Ui["admin.usenet.check.client.missing"], "/Settings/DownloadClients/Edit")
            : enabledClients.Any(card => card.Health?.IsHealthy == true)
                ? new UsenetCheck("client", UsenetCheckState.Ok, string.Join(", ", enabledClients.Where(card => card.Health?.IsHealthy == true).Select(card => card.Entry.Name)), null)
                : new UsenetCheck("client", UsenetCheckState.Warning, Ui["admin.usenet.check.client.untested"], null));

        var booksCategory = enabledClients
            .Select(card => card.Entry.Settings.BooksCategory)
            .FirstOrDefault(category => !string.IsNullOrWhiteSpace(category));
        checks.Add(enabledClients.Length == 0
            ? new UsenetCheck("booksCategory", UsenetCheckState.Unknown, Ui["admin.usenet.check.needsClient"], null)
            : booksCategory is null
                ? new UsenetCheck("booksCategory", UsenetCheckState.Warning, Ui["admin.usenet.check.booksCategory.missing"], $"/Settings/DownloadClients/Edit/{enabledClients[0].Entry.Id}")
                : new UsenetCheck("booksCategory", UsenetCheckState.Ok, booksCategory, null));

        var completed = Downloads.Where(row => row.LocalPathReadable is not null).ToArray();
        checks.Add(completed.Length == 0
            ? new UsenetCheck(
                "paths",
                UsenetCheckState.Unknown,
                RemotePathMappingCount > 0
                    ? Ui.Format("admin.usenet.check.paths.mappingsOnly", ("count", RemotePathMappingCount))
                    : Ui["admin.usenet.check.paths.noDownloads"],
                "/Settings/Acquisition")
            : completed[0].LocalPathReadable == true
                ? new UsenetCheck("paths", UsenetCheckState.Ok, completed[0].LocalPath ?? string.Empty, null)
                : new UsenetCheck(
                    "paths",
                    UsenetCheckState.Warning,
                    Ui.Format("admin.usenet.check.paths.unreadable", ("path", completed[0].StoragePath ?? string.Empty)),
                    "/Settings/Acquisition"));

        checks.Add(BookPolicy is null
            ? new UsenetCheck("access", UsenetCheckState.Unknown, string.Empty, "/Admin/Requests")
            : new UsenetCheck(
                "access",
                UsenetCheckState.Ok,
                Ui[$"admin.requests.userAdd.{AcquisitionAccessNames.UserAdd(BookPolicy.UserAdd)}"],
                "/Admin/Requests"));

        return checks;
    }
}
