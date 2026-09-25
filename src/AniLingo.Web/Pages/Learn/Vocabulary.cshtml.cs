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

    public async Task OnGetAsync(
        string? search,
        string? state,
        CancellationToken cancellationToken)
    {
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
                x.NextReviewAt != null && x.NextReviewAt <= now))
            .ToListAsync(cancellationToken);
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

    private async Task<IActionResult> ChangeAsync(
        Guid termId,
        UserTermState state,
        CancellationToken cancellationToken)
    {
        var exists = await db.Terms
            .AsNoTracking()
            .AnyAsync(x => x.Id == termId, cancellationToken);
        if (!exists)
        {
            return NotFound();
        }

        await learningService.SetStateAsync(
            termId,
            state,
            cancellationToken);

        TempData["Status"] = $"Vocabulary item moved to {state}.";
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
