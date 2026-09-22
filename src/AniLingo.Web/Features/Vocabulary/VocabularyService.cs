using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Vocabulary;

public sealed class VocabularyService(
    AppDbContext db,
    JapaneseTermExtractor extractor)
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

        var aggregate = new Dictionary<string, (int Count, int FirstMs)>(StringComparer.Ordinal);

        foreach (var cue in cues)
        {
            foreach (var canonical in extractor.Extract(cue.Text))
            {
                if (aggregate.TryGetValue(canonical, out var current))
                {
                    aggregate[canonical] = (current.Count + 1, current.FirstMs);
                }
                else
                {
                    aggregate[canonical] = (1, cue.StartMs);
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
            if (!terms.ContainsKey(canonical))
            {
                var term = new Term { Language = "ja", Canonical = canonical };
                terms.Add(canonical, term);
                db.Terms.Add(term);
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
}
