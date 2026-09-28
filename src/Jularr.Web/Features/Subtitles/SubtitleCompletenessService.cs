using Jularr.Web.Data;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Watchlist;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Subtitles;

public sealed record SubtitleWantedLanguageState(
    string LanguageTag,
    bool Forced,
    bool Sdh,
    bool Satisfied);

public sealed record SubtitleEpisodeCompletion(
    Guid EpisodeId,
    string AnimeTitle,
    int SeasonNumber,
    int EpisodeNumber,
    string EpisodeTitle,
    Guid ProfileId,
    string ProfileName,
    SubtitleProfileCompletionStatus Status,
    IReadOnlyList<SubtitleWantedLanguageState> Items,
    IReadOnlyList<SubtitleWantedLanguageState> Missing);

/// <summary>
/// Computes, per episode, which of a resolved <see cref="SubtitleLanguageProfile"/>'s wanted
/// languages are already covered by an embedded text-subtitle stream (canonical media inventory,
/// <see cref="MediaAnalysisStream"/>) or an already-imported external <see cref="SubtitleTrack"/>
/// with cues - Bazarr's "missing subtitles" tracking (#526). Anime/Episode is the only media type
/// with playable tracks today; other <see cref="WatchlistMediaType"/> values resolve a profile but
/// have no completion source wired up yet.
/// </summary>
public sealed class SubtitleCompletenessService(
    AppDbContext db,
    SubtitleLanguageProfileService profiles)
{
    public async Task<SubtitleEpisodeCompletion?> GetEpisodeCompletionAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var episode = await (
            from ep in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on ep.AnimeId equals anime.Id
            where ep.Id == episodeId
            select new
            {
                ep.Id,
                AnimeTitle = anime.Title,
                ep.SeasonNumber,
                ep.Number,
                ep.Title
            }).SingleOrDefaultAsync(cancellationToken);

        if (episode is null)
        {
            return null;
        }

        var libraryRootId = await db.MediaFiles
            .AsNoTracking()
            .Where(m => m.EpisodeId == episodeId)
            .OrderBy(m => m.Path)
            .Select(m => (Guid?)m.LibraryRootId)
            .FirstOrDefaultAsync(cancellationToken);

        var resolution = await profiles.ResolveAsync(
            WatchlistMediaType.Anime,
            libraryRootId,
            cancellationToken);

        var embeddedStreams = await (
            from mediaFile in db.MediaFiles.AsNoTracking()
            join stream in db.MediaAnalysisStreams.AsNoTracking() on mediaFile.Id equals stream.MediaFileId
            where mediaFile.EpisodeId == episodeId && stream.Kind == MediaStreamKind.Subtitle
            select new { stream.Language, stream.Title, stream.IsForced, stream.Codec }).ToListAsync(cancellationToken);

        var externalTracks = await db.SubtitleTracks
            .AsNoTracking()
            .Where(track =>
                track.EpisodeId == episodeId &&
                db.SubtitleCues.Any(cue => cue.SubtitleTrackId == track.Id))
            .Select(track => new { track.Language, track.Forced, track.Sdh })
            .ToListAsync(cancellationToken);

        var items = resolution.Items;
        var satisfiedFlags = new bool[items.Count];
        var states = new SubtitleWantedLanguageState[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            var embeddedMatch = embeddedStreams.Any(stream =>
                SubtitleFormats.IsText(stream.Codec) &&
                EmbeddedSubtitleExtractor.MatchesLanguage(stream.Language, stream.Title, item.LanguageTag) &&
                stream.IsForced == item.Forced &&
                (!item.Sdh || LooksLikeSdh(stream.Title)));

            var externalMatch = externalTracks.Any(track =>
                EmbeddedSubtitleExtractor.MatchesLanguage(track.Language, null, item.LanguageTag) &&
                track.Forced == item.Forced &&
                track.Sdh == item.Sdh);

            var satisfied = embeddedMatch || externalMatch;
            satisfiedFlags[i] = satisfied;
            states[i] = new SubtitleWantedLanguageState(item.LanguageTag, item.Forced, item.Sdh, satisfied);
        }

        var evaluation = SubtitleCompletionEvaluator.Evaluate(satisfiedFlags, resolution.Profile.CutoffPosition);
        var missing = evaluation.MissingIndexes.Select(index => states[index]).ToArray();

        return new SubtitleEpisodeCompletion(
            episode.Id,
            episode.AnimeTitle,
            episode.SeasonNumber,
            episode.Number,
            episode.Title,
            resolution.Profile.Id,
            resolution.Profile.Name,
            evaluation.Status,
            states,
            missing);
    }

    /// <summary>Every discovered episode's completion state, ordered like the library browser, bounded to <paramref name="limit"/>.</summary>
    public async Task<IReadOnlyList<SubtitleEpisodeCompletion>> GetCompletionsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var boundedLimit = Math.Clamp(limit, 1, 500);

        var episodeIds = await (
            from episode in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            where db.MediaFiles.Any(media => media.EpisodeId == episode.Id)
            orderby anime.Title, episode.SeasonNumber, episode.Number
            select episode.Id)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);

        var results = new List<SubtitleEpisodeCompletion>(episodeIds.Count);
        foreach (var episodeId in episodeIds)
        {
            var completion = await GetEpisodeCompletionAsync(episodeId, cancellationToken);
            if (completion is not null)
            {
                results.Add(completion);
            }
        }

        return results;
    }

    private static bool LooksLikeSdh(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (title.Contains("closed caption", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("hearing impaired", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Short tokens ("sdh", "cc", "hi") only count as whole words: they collide with ordinary
        // substrings ("occasion" contains "cc") when matched loosely.
        var tokens = title.Split(
            [' ', '.', '-', '_', '(', ')', '[', ']'],
            StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token =>
            token.Equals("sdh", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("cc", StringComparison.OrdinalIgnoreCase));
    }
}
