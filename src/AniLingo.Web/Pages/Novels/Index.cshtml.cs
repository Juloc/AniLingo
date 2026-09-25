using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Novels;

public sealed class IndexModel(
    NovelService novels,
    CurrentAccountContext account,
    AppDbContext db) : PageModel
{
    public IReadOnlyList<NovelListItem> Works { get; private set; } = [];
    public IReadOnlyList<NovelListItem> ContinueReading { get; private set; } = [];
    public bool IsOwner => account.IsOwner;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Works = await novels.GetWorksAsync(account.ProfileId, cancellationToken);
        ContinueReading = Works
            .Where(x => x.HasProgress)
            .OrderByDescending(x => x.LastReadAt)
            .Take(8)
            .ToArray();
    }

    public async Task<IActionResult> OnPostImportAsync(
        string sourceUrl,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var store = new OperationStore(db);
        var operationId = await store.CreateAsync(
            new OperationDescriptor(
                "novel-import",
                "Novels",
                "Import novel source",
                ProfileId: account.ProfileId,
                Lane: OperationLane.Normal,
                IsDownload: true,
                Retryable: false),
            cancellationToken);

        await store.MarkRunningAsync(operationId, cancellationToken);

        try
        {
            var workId = await novels.ImportWorkAsync(sourceUrl, cancellationToken);
            await store.MarkSucceededAsync(
                operationId,
                "Novel metadata and chapter index imported.",
                cancellationToken);

            TempData["Status"] = "Novel imported. Chapter text is loaded on demand.";
            return RedirectToPage("/Novels/Work", new { id = workId });
        }
        catch (InvalidOperationException exception)
        {
            await store.MarkFailedAsync(
                operationId,
                $"{exception.GetType().Name}: {exception.Message}",
                CancellationToken.None);
            TempData["Status"] = exception.Message;
            return RedirectToPage();
        }
    }
}
