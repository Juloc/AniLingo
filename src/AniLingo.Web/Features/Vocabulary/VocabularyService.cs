using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Vocabulary;

public sealed class VocabularyService(
    AppDbContext db,
    JapaneseTermExtractor extractor,
    JapaneseDictionary dictionary)
{
    public async Task RebuildEpisodeAsync(Guid episodeId, CancellationToken cancellationToken)
    {
        var cues = await (
            from cue in db.SubtitleCues.AsNoTracking()
            join track in db.SubtitleTracks.AsNoTracking() on cue.SubtitleTrackId equals track.Id
            where track.EpisodeId == episodeId && track.Language == "ja"
            orderby cue.StartMs
            select new { cue.Text, cue.StartMs })
            .ToListAsync(cancellationToken);

        var aggregate = new Dictionary<string, TermAggregate>(StringComparer.Ordinal);

        foreach (var cue in cues)
        {
            foreach (var candidate in extractor.Extract(cue.Text))
            {
                if (aggregate.TryGetValue(candidate.Canonical, out var current))
                {
                    aggregate[candidate.Canonical] = current with
                    {
                        Count = current.Count + 1,
                        Reading = PreferReading(current.Reading, candidate.Reading)
                    };
                }
                else
                {
                    aggregate[candidate.Canonical] = new TermAggregate(
                        Count: 1,
                        FirstMs: cue.StartMs,
                        Reading: candidate.Reading);
                }
            }
        }

        await db.EpisodeTerms
            .Where(x => x.EpisodeId == episodeId)
            .ExecuteDeleteAsync(cancellationToken);

        if (aggregate.Count == 0)
        {
            return;
        }

        var canonicalTerms = aggregate.Keys.ToArray();
        var terms = await db.Terms
            .Where(x => x.Language == "ja" && canonicalTerms.Contains(x.Canonical))
            .ToDictionaryAsync(x => x.Canonical, StringComparer.Ordinal, cancellationToken);

        foreach (var canonical in canonicalTerms)
        {
            var analyzed = aggregate[canonical];
            var dictionaryEntry = dictionary.Find(canonical);
            var reading = PreferReading(analyzed.Reading, dictionaryEntry?.Reading);
            var meaning = dictionaryEntry?.Meaning;

            if (!terms.TryGetValue(canonical, out var term))
            {
                term = new Term
                {
                    Language = "ja",
                    Canonical = canonical,
                    Reading = NullIfEmpty(reading),
                    Meaning = NullIfEmpty(meaning)
                };

                terms.Add(canonical, term);
                db.Terms.Add(term);
                continue;
            }

            if (string.IsNullOrWhiteSpace(term.Reading) && !string.IsNullOrWhiteSpace(reading))
            {
                term.Reading = reading;
            }

            if (string.IsNullOrWhiteSpace(term.Meaning) && !string.IsNullOrWhiteSpace(meaning))
            {
                term.Meaning = meaning;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        db.EpisodeTerms.AddRange(aggregate.Select(item => new EpisodeTerm
        {
            EpisodeId = episodeId,
            TermId = terms[item.Key].Id,
            Occurrences = item.Value.Count,
            FirstCueStartMs = item.Value.FirstMs
        }));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? PreferReading(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary : fallback;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record TermAggregate(int Count, int FirstMs, string? Reading);
}
