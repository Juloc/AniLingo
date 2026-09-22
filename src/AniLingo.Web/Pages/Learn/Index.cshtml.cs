using AniLingo.Web.Features.Learning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Learn;

public sealed class IndexModel(LearningService learningService) : PageModel
{
    public IReadOnlyList<DueReviewItem> Due { get; private set; } = [];
    public IReadOnlyDictionary<ReviewRating, string> Intervals { get; private set; } =
        new Dictionary<ReviewRating, string>();
    public ReviewAnimeContext? Context { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Due = await learningService.GetDueAsync(cancellationToken);

        var current = Due.FirstOrDefault();
        if (current is null)
        {
            return;
        }

        Intervals = (await learningService.GetReviewOptionsAsync(current.TermId, cancellationToken))
            .ToDictionary(option => option.Rating, option => option.IntervalLabel);
        Context = await learningService.GetReviewContextAsync(current.TermId, cancellationToken);
    }

    public async Task<IActionResult> OnPostReviewAsync(
        Guid termId,
        ReviewRating rating,
        CancellationToken cancellationToken)
    {
        await learningService.ReviewAsync(termId, rating, cancellationToken);
        return RedirectToPage();
    }

    public string Interval(ReviewRating rating) =>
        Intervals.GetValueOrDefault(rating, "—");
}
