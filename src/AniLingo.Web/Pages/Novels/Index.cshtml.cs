using AniLingo.Web.Features.Novels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Novels;

public sealed class IndexModel(NovelService novels) : PageModel
{
    public IReadOnlyList<NovelListItem> Works { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Works = await novels.GetWorksAsync(cancellationToken);

    public async Task<IActionResult> OnPostImportAsync(
        string sourceUrl,
        CancellationToken cancellationToken)
    {
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
