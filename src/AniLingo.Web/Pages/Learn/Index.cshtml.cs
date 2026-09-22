using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Learning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Learn;

public sealed class IndexModel(
    LearningService learningService,
    AiSentenceExplanationService aiExplanationService) : PageModel
{
    public IReadOnlyList<DueReviewItem> Due { get; private set; } = [];
    public IReadOnlyDictionary<ReviewRating, string> Intervals { get; private set; } =
        new Dictionary<ReviewRating, string>();
    public ReviewAnimeContext? Context { get; private set; }
    public IReadOnlyList<string> LocalHints { get; private set; } = [];
    public AiSentenceExplanation? AiExplanation { get; private set; }

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

        if (Context is not null)
        {
            LocalHints = aiExplanationService.PrepareLocal(Context.Sentence).LocalHints;
            AiExplanation = await aiExplanationService.GetCachedAsync(
                current.TermId,
                Context.Sentence,
                cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostExplainAsync(
        Guid termId,
        CancellationToken cancellationToken)
    {
        var context = await learningService.GetReviewContextAsync(termId, cancellationToken);
        if (context is null)
        {
            TempData["Status"] = "No anime sentence is available for this term.";
            return RedirectToPage();
        }

        try
        {
            await aiExplanationService.ExplainAsync(
                termId,
                context.Sentence,
                cancellationToken);
            TempData["Status"] = "AI explanation ready.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
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
