using System.Text;
using System.Text.Json;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Kana;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Kana;

public sealed class IndexModel(
    AppDbContext db,
    LearningService learningService,
    CurrentAccountContext currentAccount,
    IJapaneseMorphology morphology,
    JapaneseDictionary dictionary) : PageModel
{
    public KanaScript SelectedScript { get; private set; } = KanaScript.Hiragana;
    public int Stage { get; private set; } = 1;
    public string StageTitle => KanaCatalog.StageTitle(SelectedScript, Stage);
    public IReadOnlyList<KanaCardView> Cards { get; private set; } = [];
    public IReadOnlyList<SentencePracticeView> Sentences { get; private set; } = [];
    public int LearningCount { get; private set; }
    public int KnownCount { get; private set; }
    public int DueCount { get; private set; }
    public string CatalogJson { get; private set; } = "[]";

    public async Task OnGetAsync(string? script, int stage = 1, CancellationToken cancellationToken = default)
    {
        SelectedScript = KanaCatalog.ParseScript(script);
        Stage = Math.Clamp(stage, 1, KanaCatalog.MaxStage);

        await EnsureKanaTermsAsync(cancellationToken);
        Cards = await LoadCardsAsync(SelectedScript, Stage, cancellationToken);
        await LoadSummaryAsync(cancellationToken);
        Sentences = await LoadSentencePracticeAsync(cancellationToken);

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
        TempData["Status"] = "Kana-Gruppe zum Lernen hinzugefügt.";
        return RedirectToPage(new { script = selectedScript == KanaScript.Hiragana ? "hiragana" : "katakana", stage = selectedStage });
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
        await learningService.SetStateAsync(termId, UserTermState.Known, cancellationToken);
        TempData["Status"] = entry.Symbol + " als sicher markiert.";

        return RedirectToPage(new
        {
            script = KanaCatalog.ParseScript(script) == KanaScript.Hiragana ? "hiragana" : "katakana",
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
            .Where(x => x.ProfileId == currentAccount.ProfileId && x.TermId == termId)
            .Select(x => (UserTermState?)x.State)
            .SingleOrDefaultAsync(cancellationToken);

        if (existingState != UserTermState.Known)
        {
            await learningService.SetStateAsync(termId, UserTermState.Learning, cancellationToken);
            await learningService.ReviewAsync(
                termId,
                correct ? ReviewRating.Good : ReviewRating.Again,
                cancellationToken);
        }

        return new JsonResult(new
        {
            correct,
            expected = KanaPractice.Expected(entry, practiceMode),
            state = existingState == UserTermState.Known ? "known" : "learning"
        });
    }

    private async Task EnsureKanaTermsAsync(CancellationToken cancellationToken)
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
            db.Terms.Add(new Term
            {
                Id = KanaCatalog.IdFor(entry),
                Language = KanaCatalog.Language,
                Canonical = entry.Symbol,
                Reading = entry.Romaji,
                Meaning = (entry.Script == KanaScript.Hiragana ? "Hiragana" : "Katakana") + " · " + entry.Group
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
            .Where(x => x.ProfileId == currentAccount.ProfileId && ids.Contains(x.TermId))
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
                var due = item is { State: UserTermState.Learning, NextReviewAt: not null }
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

    private async Task LoadSummaryAsync(CancellationToken cancellationToken)
    {
        var ids = KanaCatalog.All.Select(KanaCatalog.IdFor).ToArray();
        var now = DateTime.UtcNow;
        var rows = await db.UserTerms
            .AsNoTracking()
            .Where(x => x.ProfileId == currentAccount.ProfileId && ids.Contains(x.TermId))
            .Select(x => new { x.State, x.NextReviewAt })
            .ToListAsync(cancellationToken);

        LearningCount = rows.Count(x => x.State == UserTermState.Learning);
        KnownCount = rows.Count(x => x.State == UserTermState.Known);
        DueCount = rows.Count(x =>
            x.State == UserTermState.Learning
            && x.NextReviewAt != null
            && x.NextReviewAt <= now);
    }

    private async Task<IReadOnlyList<SentencePracticeView>> LoadSentencePracticeAsync(
        CancellationToken cancellationToken)
    {
        var rows = await (
            from userTerm in db.UserTerms.AsNoTracking()
            join term in db.Terms.AsNoTracking() on userTerm.TermId equals term.Id
            join episodeTerm in db.EpisodeTerms.AsNoTracking() on term.Id equals episodeTerm.TermId
            join episode in db.Episodes.AsNoTracking() on episodeTerm.EpisodeId equals episode.Id
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            join track in db.SubtitleTracks.AsNoTracking() on episode.Id equals track.EpisodeId
            join cue in db.SubtitleCues.AsNoTracking() on track.Id equals cue.SubtitleTrackId
            where userTerm.ProfileId == currentAccount.ProfileId
                && term.Language == "ja"
                && track.Language == "ja"
                && cue.StartMs == episodeTerm.FirstCueStartMs
            orderby userTerm.State == UserTermState.Learning ? 0 : 1,
                episodeTerm.Occurrences descending,
                track.ImportedAt descending
            select new SentenceRow(
                episode.Id,
                anime.Title,
                episode.SeasonNumber,
                episode.Number,
                episode.Title,
                cue.StartMs,
                cue.Text,
                term.Canonical,
                term.Reading,
                term.Meaning))
            .Take(80)
            .ToListAsync(cancellationToken);

        var result = BuildSentenceViews(rows, 6);
        if (result.Count > 0)
        {
            return result;
        }

        var fallbackRows = await (
            from track in db.SubtitleTracks.AsNoTracking()
            join cue in db.SubtitleCues.AsNoTracking() on track.Id equals cue.SubtitleTrackId
            join episode in db.Episodes.AsNoTracking() on track.EpisodeId equals episode.Id
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            where track.Language == "ja"
                && cue.Text.Length >= 2
                && cue.Text.Length <= 90
            orderby cue.Text.Length,
                track.ImportedAt descending,
                anime.Title,
                episode.Number
            select new SentenceRow(
                episode.Id,
                anime.Title,
                episode.SeasonNumber,
                episode.Number,
                episode.Title,
                cue.StartMs,
                cue.Text,
                null,
                null,
                null))
            .Take(100)
            .ToListAsync(cancellationToken);

        return BuildSentenceViews(fallbackRows, 6);
    }

    private IReadOnlyList<SentencePracticeView> BuildSentenceViews(
        IReadOnlyList<SentenceRow> rows,
        int limit)
    {
        var result = new List<SentencePracticeView>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!KanaPractice.IsSuitableSentence(row.Text))
            {
                continue;
            }

            var key = row.EpisodeId + ":" + row.CueStartMs + ":" + row.Text;
            if (!seen.Add(key))
            {
                continue;
            }

            var analyzed = morphology.Analyze(row.Text);
            var tokens = analyzed
                .Select(token =>
                {
                    var reading = token.Reading is "*" or ""
                        ? token.Surface
                        : JapaneseTermExtractor.ToHiragana(token.Reading);
                    var dictionaryEntry = KanaPractice.IsJapaneseCharacter(token.Surface.FirstOrDefault())
                        ? dictionary.Find(token.Canonical)
                        : null;
                    return new SentenceTokenView(
                        token.Surface,
                        token.Canonical,
                        reading,
                        dictionaryEntry?.Meaning,
                        token.Surface.Any(KanaPractice.IsJapaneseCharacter),
                        string.Equals(token.Canonical, row.TargetCanonical, StringComparison.Ordinal));
                })
                .ToArray();

            var cloze = BuildCloze(analyzed, row.TargetCanonical);
            result.Add(new SentencePracticeView(
                row.EpisodeId,
                row.AnimeTitle,
                row.SeasonNumber,
                row.EpisodeNumber,
                row.EpisodeTitle,
                row.CueStartMs,
                row.Text.Trim(),
                cloze,
                row.TargetCanonical,
                row.TargetReading,
                row.TargetMeaning,
                tokens));

            if (result.Count >= limit)
            {
                break;
            }
        }

        return result;
    }

    private static string? BuildCloze(
        IReadOnlyList<JapaneseMorphToken> tokens,
        string? targetCanonical)
    {
        if (string.IsNullOrWhiteSpace(targetCanonical))
        {
            return null;
        }

        var builder = new StringBuilder();
        var replaced = false;

        foreach (var token in tokens)
        {
            if (!replaced && string.Equals(token.Canonical, targetCanonical, StringComparison.Ordinal))
            {
                builder.Append("＿＿");
                replaced = true;
            }
            else
            {
                builder.Append(token.Surface);
            }
        }

        return replaced ? builder.ToString() : null;
    }

    private sealed record SentenceRow(
        Guid EpisodeId,
        string AnimeTitle,
        int SeasonNumber,
        int EpisodeNumber,
        string EpisodeTitle,
        int CueStartMs,
        string Text,
        string? TargetCanonical,
        string? TargetReading,
        string? TargetMeaning);
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

public sealed record SentenceTokenView(
    string Surface,
    string Canonical,
    string Reading,
    string? Meaning,
    bool Interactive,
    bool IsTarget);

public sealed record SentencePracticeView(
    Guid EpisodeId,
    string AnimeTitle,
    int SeasonNumber,
    int EpisodeNumber,
    string EpisodeTitle,
    int CueStartMs,
    string Text,
    string? Cloze,
    string? TargetCanonical,
    string? TargetReading,
    string? TargetMeaning,
    IReadOnlyList<SentenceTokenView> Tokens)
{
    public string TimestampLabel
    {
        get
        {
            var time = TimeSpan.FromMilliseconds(Math.Max(0, CueStartMs));
            return time.TotalHours >= 1
                ? ((int)time.TotalHours) + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00")
                : ((int)time.TotalMinutes) + ":" + time.Seconds.ToString("00");
        }
    }
}
