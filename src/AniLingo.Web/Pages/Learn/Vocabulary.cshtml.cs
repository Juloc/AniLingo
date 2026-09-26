using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Learn;

public sealed class VocabularyModel(
    AppDbContext db,
    LearningService learningService,
    CurrentAccountContext currentAccount) : PageModel
{
    public IReadOnlyList<VocabularyRow> Items { get; private set; } = [];
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public string? Search { get; private set; }
    public string StateFilter { get; private set; } = "all";

    /// <summary>
    /// Spaced repetition resolved on for the profile. Without it words can be
    /// saved, marked known or ignored, but never enter the review queue.
    /// </summary>
    public bool ShowReviewActions { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        string? search,
        string? state,
        CancellationToken cancellationToken)
    {
        var resolved = await LearningModuleGate.ResolveAsync(
            db,
            currentAccount.ProfileId,
            cancellationToken);
        if (!resolved.IsEnabled(LearningCapability.Vocabulary))
        {
            return LearningModuleGate.RedirectToHub();
        }

        ShowReviewActions = resolved.IsEnabled(LearningCapability.Reviews);

        Ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);

        Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        StateFilter = NormalizeFilter(state);

        var query =
            from userTerm in db.UserTerms.AsNoTracking()
            join term in db.Terms.AsNoTracking()
                on userTerm.TermId equals term.Id
            where userTerm.ProfileId == currentAccount.ProfileId
            select new
            {
                userTerm.TermId,
                userTerm.State,
                userTerm.NextReviewAt,
                userTerm.UpdatedAt,
                term.Language,
                term.Canonical,
                term.Reading,
                term.Meaning
            };

        if (TryParseState(StateFilter, out var parsedState))
        {
            query = query.Where(x => x.State == parsedState);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search;
            query = query.Where(x =>
                x.Canonical.Contains(term)
                || (x.Reading != null && x.Reading.Contains(term))
                || (x.Meaning != null && x.Meaning.Contains(term)));
        }

        var now = DateTime.UtcNow;
        var showDue = ShowReviewActions;
        Items = await query
            .OrderBy(x =>
                x.State == UserTermState.Learning ? 0 :
                x.State == UserTermState.Saved ? 1 :
                x.State == UserTermState.Known ? 2 :
                x.State == UserTermState.Suspended ? 3 : 4)
            .ThenByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Canonical)
            .Take(250)
            .Select(x => new VocabularyRow(
                x.TermId,
                x.Language,
                x.Canonical,
                x.Reading,
                x.Meaning,
                x.State,
                showDue && x.NextReviewAt != null && x.NextReviewAt <= now))
            .ToListAsync(cancellationToken);

        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync(
        Guid termId,
        CancellationToken cancellationToken) =>
        ChangeAsync(termId, UserTermState.Saved, cancellationToken);

    public Task<IActionResult> OnPostLearnAsync(
        Guid termId,
        CancellationToken cancellationToken) =>
        ChangeAsync(termId, UserTermState.Learning, cancellationToken);

    public Task<IActionResult> OnPostKnownAsync(
        Guid termId,
        CancellationToken cancellationToken) =>
        ChangeAsync(termId, UserTermState.Known, cancellationToken);

    public Task<IActionResult> OnPostIgnoreAsync(
        Guid termId,
        CancellationToken cancellationToken) =>
        ChangeAsync(termId, UserTermState.Ignored, cancellationToken);

    public Task<IActionResult> OnPostSuspendAsync(
        Guid termId,
        CancellationToken cancellationToken) =>
        ChangeAsync(termId, UserTermState.Suspended, cancellationToken);

    public static string StateKey(UserTermState state) =>
        state switch
        {
            UserTermState.Known => "learn.vocabulary.state.known",
            UserTermState.Learning => "learn.vocabulary.state.learning",
            UserTermState.Saved => "learn.vocabulary.state.saved",
            UserTermState.Ignored => "learn.vocabulary.state.ignored",
            UserTermState.Suspended => "learn.vocabulary.state.suspended",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };

    private async Task<IActionResult> ChangeAsync(
        Guid termId,
        UserTermState state,
        CancellationToken cancellationToken)
    {
        var resolved = await LearningModuleGate.ResolveAsync(
            db,
            currentAccount.ProfileId,
            cancellationToken);
        if (!resolved.IsEnabled(LearningCapability.Vocabulary))
        {
            return Forbid();
        }

        // Learning and Suspended are review-queue states; they need SRS.
        if (state is UserTermState.Learning or UserTermState.Suspended
            && !resolved.IsEnabled(LearningCapability.Reviews))
        {
            return Forbid();
        }

        var canonical = await db.Terms
            .AsNoTracking()
            .Where(x => x.Id == termId)
            .Select(x => x.Canonical)
            .SingleOrDefaultAsync(cancellationToken);
        if (canonical is null)
        {
            return NotFound();
        }

        await learningService.SetStateAsync(
            termId,
            state,
            cancellationToken);

        var ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);
        TempData["Status"] = ui.Format(
            "learn.vocabulary.moved",
            ("term", canonical),
            ("state", ui[StateKey(state)]));
        return RedirectToPage();
    }

    private static string NormalizeFilter(string? state) =>
        TryParseState(state, out var parsed)
            ? parsed.ToString().ToLowerInvariant()
            : "all";

    private static bool TryParseState(
        string? value,
        out UserTermState state)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<UserTermState>(
                value,
                ignoreCase: true,
                out state))
        {
            return true;
        }

        state = default;
        return false;
    }
}

public sealed record VocabularyRow(
    Guid TermId,
    string Language,
    string Canonical,
    string? Reading,
    string? Meaning,
    UserTermState State,
    bool Due);
