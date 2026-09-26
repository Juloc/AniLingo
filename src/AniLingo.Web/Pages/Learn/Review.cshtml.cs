using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Learn;

public sealed class ReviewModel(
    AppDbContext db,
    LearningService learningService,
    AiSentenceExplanationService aiExplanationService,
    CurrentAccountContext currentAccount) : PageModel
{
    public IReadOnlyList<ReviewSessionCard> Session { get; private set; } = [];
    public ReviewSessionCard? Current => Session.FirstOrDefault();
    public IReadOnlyList<string> LocalHints { get; private set; } = [];
    public AiSentenceExplanation? AiExplanation { get; private set; }
    public string ProfileId => currentAccount.ProfileId;
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await ReviewsEnabledAsync(cancellationToken))
        {
            return LearningModuleGate.RedirectToHub();
        }

        Ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);
        Session = await learningService.GetReviewSessionAsync(cancellationToken);

        if (Current?.Context is not { } context)
        {
            return Page();
        }

        LocalHints = aiExplanationService.PrepareLocal(context.Sentence).LocalHints;
        AiExplanation = await aiExplanationService.GetCachedAsync(
            context.Sentence,
            cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostExplainAsync(
        Guid termId,
        CancellationToken cancellationToken)
    {
        if (!await ReviewsEnabledAsync(cancellationToken))
        {
            return Forbid();
        }

        var context = await learningService.GetReviewContextAsync(termId, cancellationToken);
        if (context is null)
        {
            TempData["Status"] = "No anime sentence is available for this term.";
            return RedirectToPage();
        }

        try
        {
            await aiExplanationService.ExplainAsync(
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
        if (!await ReviewsEnabledAsync(cancellationToken))
        {
            return Forbid();
        }

        await learningService.ReviewAsync(termId, rating, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSyncOfflineReviewsAsync(
        [FromBody] OfflineReviewSyncRequest request,
        CancellationToken cancellationToken)
    {
        if (!await ReviewsEnabledAsync(cancellationToken))
        {
            return Forbid();
        }

        if (request.Events is null)
        {
            return BadRequest();
        }

        var result = await learningService.SyncOfflineReviewsAsync(
            request.Events,
            DateTime.UtcNow,
            cancellationToken);

        return new JsonResult(result);
    }

    private async Task<bool> ReviewsEnabledAsync(CancellationToken cancellationToken)
    {
        var resolved = await LearningModuleGate.ResolveAsync(
            db,
            currentAccount.ProfileId,
            cancellationToken);
        return resolved.IsEnabled(LearningCapability.Reviews);
    }
}
