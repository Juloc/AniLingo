using System.Text.Json;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Kana;
using AniLingo.Web.Features.Learning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Kana;

public sealed class IndexModel(
    AppDbContext db,
    LearningService learningService,
    CurrentAccountContext currentAccount) : PageModel
{
    public KanaScript SelectedScript { get; private set; } = KanaScript.Hiragana;
    public int Stage { get; private set; } = 1;
    public string StageTitle => KanaCatalog.StageTitle(SelectedScript, Stage);
    public IReadOnlyList<KanaCardView> Cards { get; private set; } = [];
    public int LearningCount { get; private set; }
    public int KnownCount { get; private set; }
    public int DueCount { get; private set; }
    public string CatalogJson { get; private set; } = "[]";

    public async Task OnGetAsync(
        string? script,
        int stage = 1,
        CancellationToken cancellationToken = default)
    {
        SelectedScript = KanaCatalog.ParseScript(script);
        Stage = Math.Clamp(stage, 1, KanaCatalog.MaxStage);

        await EnsureKanaTermsAsync(cancellationToken);
        Cards = await LoadCardsAsync(SelectedScript, Stage, cancellationToken);
        await LoadSummaryAsync(cancellationToken);

        CatalogJson = JsonSerializer.Serialize(
            Cards.Select(card => new
            {
                termId = card.TermId,
                symbol = card.Symbol,
                romaji = card.Romaji,
                group = card.Group,
                stage = card.Stage,
                script = card.ScriptKey,
                state = card.State
            }),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public async Task<IActionResult> OnPostStartGroupAsync(
        string? script,
        int stage,
        CancellationToken cancellationToken)
    {
        var selectedScript = KanaCatalog.ParseScript(script);
        var selectedStage = Math.Clamp(stage, 1, KanaCatalog.MaxStage);
        await EnsureKanaTermsAsync(cancellationToken);

        var termIds = KanaCatalog.ForStage(selectedScript, selectedStage)
            .Select(KanaCatalog.IdFor)
            .ToArray();

        await learningService.AddToLearningAsync(termIds, cancellationToken);
        TempData["Status"] = "Kana group added to active learning.";
        return RedirectToPage(new
        {
            script = selectedScript == KanaScript.Hiragana ? "hiragana" : "katakana",
            stage = selectedStage
        });
    }

    public async Task<IActionResult> OnPostKnownAsync(
        Guid termId,
        string? script,
        int stage,
        CancellationToken cancellationToken)
    {
        var entry = KanaCatalog.Find(termId);
        if (entry is null)
        {
            return BadRequest();
        }

        await EnsureKanaTermsAsync(cancellationToken);
        await learningService.SetStateAsync(
            termId,
            UserTermState.Known,
            cancellationToken);

        TempData["Status"] = entry.Symbol + " marked as known.";

        return RedirectToPage(new
        {
            script = KanaCatalog.ParseScript(script) == KanaScript.Hiragana
                ? "hiragana"
                : "katakana",
            stage = Math.Clamp(stage, 1, KanaCatalog.MaxStage)
        });
    }

    public async Task<IActionResult> OnPostAnswerAsync(
        Guid termId,
        string? mode,
        string? answer,
        CancellationToken cancellationToken)
    {
        var entry = KanaCatalog.Find(termId);
        if (entry is null || !KanaPractice.TryParseMode(mode, out var practiceMode))
        {
            return BadRequest();
        }

        await EnsureKanaTermsAsync(cancellationToken);
        var correct = KanaPractice.IsCorrect(entry, practiceMode, answer);

        var existingState = await db.UserTerms
            .AsNoTracking()
            .Where(x =>
                x.ProfileId == currentAccount.ProfileId
                && x.TermId == termId)
            .Select(x => (UserTermState?)x.State)
            .SingleOrDefaultAsync(cancellationToken);

        if (existingState != UserTermState.Known)
        {
            await learningService.SetStateAsync(
                termId,
                UserTermState.Learning,
                cancellationToken);
            await learningService.ReviewAsync(
                termId,
                correct ? ReviewRating.Good : ReviewRating.Again,
                cancellationToken);
        }

        return new JsonResult(new
        {
            correct,
            expected = KanaPractice.Expected(entry, practiceMode),
            state = existingState == UserTermState.Known
                ? "known"
                : "learning"
        });
    }

    private async Task EnsureKanaTermsAsync(
        CancellationToken cancellationToken)
    {
        var existing = await db.Terms
            .Where(x => x.Language == KanaCatalog.Language)
            .Select(x => x.Canonical)
            .ToListAsync(cancellationToken);

        var existingSet = existing.ToHashSet(StringComparer.Ordinal);
        var missing = KanaCatalog.All
            .Where(entry => !existingSet.Contains(entry.Symbol))
            .ToArray();

        if (missing.Length == 0)
        {
            return;
        }

        foreach (var entry in missing)
        {
            db.Terms.Add(new Features.Vocabulary.Term
            {
                Id = KanaCatalog.IdFor(entry),
                Language = KanaCatalog.Language,
                Canonical = entry.Symbol,
                Reading = entry.Romaji,
                Meaning =
                    (entry.Script == KanaScript.Hiragana
                        ? "Hiragana"
                        : "Katakana")
                    + " · "
                    + entry.Group
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<KanaCardView>> LoadCardsAsync(
        KanaScript script,
        int stage,
        CancellationToken cancellationToken)
    {
        var entries = KanaCatalog.ForStage(script, stage);
        var ids = entries.Select(KanaCatalog.IdFor).ToArray();

        var progress = await db.UserTerms
            .AsNoTracking()
            .Where(x =>
                x.ProfileId == currentAccount.ProfileId
                && ids.Contains(x.TermId))
            .ToDictionaryAsync(x => x.TermId, cancellationToken);

        var now = DateTime.UtcNow;
        return entries
            .Select(entry =>
            {
                var id = KanaCatalog.IdFor(entry);
                progress.TryGetValue(id, out var item);
                var state = item?.State switch
                {
                    UserTermState.Known => "known",
                    UserTermState.Learning => "learning",
                    _ => "new"
                };
                var due =
                    item is
                    {
                        State: UserTermState.Learning,
                        NextReviewAt: not null
                    }
                    && item.NextReviewAt <= now;

                return new KanaCardView(
                    id,
                    entry.Symbol,
                    entry.Romaji,
                    entry.Group,
                    entry.Stage,
                    entry.ScriptKey,
                    state,
                    due);
            })
            .ToArray();
    }

    private async Task LoadSummaryAsync(
        CancellationToken cancellationToken)
    {
        var ids = KanaCatalog.All.Select(KanaCatalog.IdFor).ToArray();
        var now = DateTime.UtcNow;

        var rows = await db.UserTerms
            .AsNoTracking()
            .Where(x =>
                x.ProfileId == currentAccount.ProfileId
                && ids.Contains(x.TermId))
            .Select(x => new { x.State, x.NextReviewAt })
            .ToListAsync(cancellationToken);

        LearningCount = rows.Count(x => x.State == UserTermState.Learning);
        KnownCount = rows.Count(x => x.State == UserTermState.Known);
        DueCount = rows.Count(x =>
            x.State == UserTermState.Learning
            && x.NextReviewAt != null
            && x.NextReviewAt <= now);
    }
}

public sealed record KanaCardView(
    Guid TermId,
    string Symbol,
    string Romaji,
    string Group,
    int Stage,
    string ScriptKey,
    string State,
    bool Due);
