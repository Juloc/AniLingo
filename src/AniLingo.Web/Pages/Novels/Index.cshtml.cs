using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Novels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Novels;

public sealed class IndexModel(NovelService novels, CurrentAccountContext account) : PageModel
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

        try
        {
            var workId = await novels.ImportWorkAsync(sourceUrl, cancellationToken);
            TempData["Status"] = "Novel imported. Chapter text is loaded on demand.";
            return RedirectToPage("/Novels/Work", new { id = workId });
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage();
        }
    }
}
