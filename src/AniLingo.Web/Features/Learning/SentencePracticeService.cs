using System.Text;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Kana;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Learning;

public sealed class SentencePracticeService(
    AppDbContext db,
    string profileId,
    IJapaneseMorphology morphology,
    JapaneseDictionary dictionary)
{
    public async Task<IReadOnlyList<SentencePracticeItem>> LoadAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 50);

        var rows = await (
            from termState in LearningQueries.TermStates(db, profileId)
            join term in db.Terms.AsNoTracking() on termState.TermId equals term.Id
            join episodeTerm in db.EpisodeTerms.AsNoTracking() on term.Id equals episodeTerm.TermId
            join episode in db.Episodes.AsNoTracking() on episodeTerm.EpisodeId equals episode.Id
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            join track in db.SubtitleTracks.AsNoTracking() on episode.Id equals track.EpisodeId
            join cue in db.SubtitleCues.AsNoTracking() on track.Id equals cue.SubtitleTrackId
            where termState.SentencePracticeEnabled
                && termState.State != UserTermState.Ignored
                && term.Language == "ja"
                && track.Language == "ja"
                && cue.StartMs == episodeTerm.FirstCueStartMs
            orderby termState.State == UserTermState.Learning ? 0 :
                    termState.State == UserTermState.Saved ? 1 :
                    termState.State == UserTermState.Known ? 2 : 3,
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
            .Take(Math.Max(80, limit * 10))
            .ToListAsync(cancellationToken);

        var result = BuildSentenceViews(rows, limit);
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
            .Take(Math.Max(100, limit * 12))
            .ToListAsync(cancellationToken);

        return BuildSentenceViews(fallbackRows, limit);
    }

    private IReadOnlyList<SentencePracticeItem> BuildSentenceViews(
        IReadOnlyList<SentenceRow> rows,
        int limit)
    {
        var result = new List<SentencePracticeItem>();
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
                    var dictionaryEntry =
                        KanaPractice.IsJapaneseCharacter(token.Surface.FirstOrDefault())
                            ? dictionary.Find(token.Canonical)
                            : null;

                    return new SentencePracticeToken(
                        token.Surface,
                        token.Canonical,
                        reading,
                        dictionaryEntry?.Meaning,
                        token.Surface.Any(KanaPractice.IsJapaneseCharacter),
                        string.Equals(
                            token.Canonical,
                            row.TargetCanonical,
                            StringComparison.Ordinal));
                })
                .ToArray();

            result.Add(new SentencePracticeItem(
                row.EpisodeId,
                row.AnimeTitle,
                row.SeasonNumber,
                row.EpisodeNumber,
                row.EpisodeTitle,
                row.CueStartMs,
                row.Text.Trim(),
                BuildCloze(analyzed, row.TargetCanonical),
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
            if (!replaced
                && string.Equals(
                    token.Canonical,
                    targetCanonical,
                    StringComparison.Ordinal))
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

public sealed record SentencePracticeToken(
    string Surface,
    string Canonical,
    string Reading,
    string? Meaning,
    bool Interactive,
    bool IsTarget);

public sealed record SentencePracticeItem(
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
    IReadOnlyList<SentencePracticeToken> Tokens)
{
    public string TimestampLabel
    {
        get
        {
            var time = TimeSpan.FromMilliseconds(Math.Max(0, CueStartMs));
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
        }
    }
}
