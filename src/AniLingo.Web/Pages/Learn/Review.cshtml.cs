using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Learning.LanguageAssistance;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Novels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Learn;

/// <summary>
/// Return-to-source link for a review card whose word came from a
/// non-anime reader (issue #147): the most recent <see cref="LearningContext"/>
/// the profile recorded for the term's unit, resolved back to a chapter and
/// paragraph anchor the reader can jump straight to.
/// </summary>
public sealed record ReviewSourceLink(string Label, string Url, string? Sentence);

public sealed class ReviewModel(
    AppDbContext db,
    LearningService learningService,
    AiSentenceExplanationService aiExplanationService,
    NovelCatalogQueries novels,
    CurrentAccountContext currentAccount) : PageModel
{
    public IReadOnlyList<ReviewSessionCard> Session { get; private set; } = [];
    public ReviewSessionCard? Current => Session.FirstOrDefault();
    public IReadOnlyList<string> LocalHints { get; private set; } = [];
    public AiSentenceExplanation? AiExplanation { get; private set; }
    public ReviewSourceLink? SourceLink { get; private set; }
    public string ProfileId => currentAccount.ProfileId;
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public string ModeLabel(LearningCardMode mode) =>
        Ui[ModeKey(mode)];

    public IReadOnlyDictionary<string, string> ModeLabels =>
        Enum.GetValues<LearningCardMode>()
            .ToDictionary(mode => mode.ToString(), ModeLabel);

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
            if (Current?.TermId is { } uncontextedTermId)
            {
                SourceLink = await ResolveNovelSourceLinkAsync(uncontextedTermId, cancellationToken);
            }

            return Page();
        }

        LocalHints = aiExplanationService.PrepareLocal(context.Sentence).LocalHints;
        AiExplanation = await aiExplanationService.GetCachedAsync(
            context.Sentence,
            cancellationToken);
        return Page();
    }

    /// <summary>
    /// The novel reader (issue #147) records a source-agnostic
    /// <see cref="LearningContext"/> (<c>chapter:{id}</c> + <c>paragraph:{i}</c>)
    /// instead of the legacy anime-only <see cref="ReviewAnimeContext"/>. This
    /// resolves the most recent one back to a chapter/paragraph the reader can
    /// jump straight to, for review items that came from a novel rather than
    /// an anime episode.
    /// </summary>
    private async Task<ReviewSourceLink?> ResolveNovelSourceLinkAsync(
        Guid termId,
        CancellationToken cancellationToken)
    {
        var unitId = await db.LearningUnits
            .AsNoTracking()
            .Where(x => x.TermId == termId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (unitId is not { } unit)
        {
            return null;
        }

        var learningContext = await db.LearningContexts
            .AsNoTracking()
            .Where(x => x.ProfileId == currentAccount.ProfileId
                && x.UnitId == unit
                && x.SourceType == "novel")
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (learningContext is null)
        {
            return null;
        }

        if (!LanguageSourceAnchor.TryParseStored(
                learningContext.SourceType,
                learningContext.SourceKey,
                learningContext.PositionKey,
                out _,
                out var chapterId,
                out var position))
        {
            return null;
        }

        var chapter = await novels.GetChapterContextAsync(chapterId, cancellationToken);
        if (chapter is null)
        {
            return null;
        }

        var url = position.Paragraph is { } paragraph
            ? $"/Novels/Read/{chapterId}?paragraph={paragraph}&lang=ja"
            : $"/Novels/Read/{chapterId}";
        return new ReviewSourceLink(
            $"{chapter.WorkTitle} · Kapitel {chapter.Number}",
            url,
            learningContext.Text);
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
        Guid cardId,
        ReviewRating rating,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(rating))
        {
            return BadRequest();
        }

        if (!await ReviewsEnabledAsync(cancellationToken))
        {
            return Forbid();
        }

        try
        {
            await learningService.ReviewAsync(cardId, rating, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            TempData["Status"] = "This card is no longer due.";
        }

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
        return resolved.Reviews;
    }

    private static string ModeKey(LearningCardMode mode) =>
        mode switch
        {
            LearningCardMode.Production => "learn.mode.production",
            LearningCardMode.Listening => "learn.mode.listening",
            LearningCardMode.Writing => "learn.mode.writing",
            _ => "learn.mode.recognition"
        };
}
