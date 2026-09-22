using AniLingo.Web.Features.Learning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Learn;

public sealed class IndexModel(LearningService learningService) : PageModel
{
    public IReadOnlyList<DueReviewItem> Due { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Due = await learningService.GetDueAsync(50, cancellationToken);
    }

    public async Task<IActionResult> OnPostReviewAsync(
        Guid termId,
        ReviewRating rating,
        CancellationToken cancellationToken)
    {
        await learningService.ReviewAsync(termId, rating, cancellationToken);
        return RedirectToPage();
    }
}
