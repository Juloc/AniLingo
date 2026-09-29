using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Artwork;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Playback;
using Jularr.Web.Features.Progress;
using Jularr.Web.Ui;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Library;

/// <summary>
/// Profile-scoped read model behind the Library "Media Banner" cards (#395). It loads the whole
/// grid with a fixed number of set-based queries (titles, episodes, progress, embedded streams,
/// sidecar subtitles) regardless of library size, then assembles each card in memory.
/// <list type="bullet">
/// <item>Status, rating and year come from the matched provider metadata; the year falls back to
/// the local tvshow.nfo.</item>
/// <item>Audio and subtitle languages come from the media inventory (probed streams) plus
/// imported subtitle tracks, most common first.</item>
/// <item>Progress and the next episode come from canonical <see cref="EpisodeProgress"/> rows;
/// see <see cref="ResolveNext"/>.</item>
/// <item>Artwork goes through <see cref="AnimeArtworkStore"/> (fanart/banner, poster as fallback).</item>
/// </list>
/// </summary>
public sealed class LibraryMediaCardQuery(AppDbContext db)
{
    public async Task<IReadOnlyList<MediaBannerCardData>> GetAnimeAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var titles = await (
            from anime in db.Anime.AsNoTracking()
            join metadataValue in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadataValue.AnimeId into metadataRows
            from metadata in metadataRows.DefaultIfEmpty()
            join localValue in db.AnimeLocalMetadata.AsNoTracking()
                on anime.Id equals localValue.AnimeId into localRows
            from local in localRows.DefaultIfEmpty()
            orderby metadata == null ? anime.Title : metadata.PreferredTitle
            select new TitleRow(
                anime.Id,
                metadata == null ? anime.Title : metadata.PreferredTitle,
                metadata == null ? null : metadata.Status,
                metadata != null && metadata.SeasonYear != null
                    ? metadata.SeasonYear
                    : local == null ? null : local.Year,
                metadata == null ? null : metadata.AverageScore,
                metadata == null ? null : metadata.EpisodeCount,
                metadata == null ? null : metadata.BannerImageUrl,
                metadata == null ? null : metadata.CoverImageUrl,
                metadata == null ? null : metadata.Provider,
                metadata == null ? null : metadata.ExternalId))
            .ToListAsync(cancellationToken);

        if (titles.Count == 0)
        {
            return [];
        }

        // The stage of an open request per AniList id, for the availability badge (#597).
        var openRequests = (await new AcquisitionAccessStore(db).ListAsync(
                MediaAcquisitionKind.Anime,
                requestedByProfileId: null,
                openOnly: true,
                limit: 500,
                cancellationToken))
            .Where(request => request.Provider == AniListMetadataProvider.ProviderKey)
            .GroupBy(request => request.ExternalId)
            .ToDictionary(group => group.Key, group => group.First().Status);

        var episodes = (await db.Episodes
                .AsNoTracking()
                .Select(x => new EpisodeRow(
                    x.Id,
                    x.AnimeId,
                    x.SeasonNumber,
                    x.Number,
                    db.MediaFiles.Any(media => media.EpisodeId == x.Id)))
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.AnimeId);

        var progress = (await db.EpisodeProgress
                .AsNoTracking()
                .Where(x => x.ProfileId == profileId)
                .Select(x => new EpisodeProgressState(x.EpisodeId, x.PositionMs, x.IsCompleted, x.UpdatedAt))
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.EpisodeId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(row => row.UpdatedAt).First());

        var streams = await (
            from stream in db.MediaAnalysisStreams.AsNoTracking()
            join media in db.MediaFiles.AsNoTracking() on stream.MediaFileId equals media.Id
            join episode in db.Episodes.AsNoTracking() on media.EpisodeId equals episode.Id
            where stream.Language != null
            group stream by new { episode.AnimeId, stream.Kind, stream.Language } into languageGroup
            select new LanguageRow(
                languageGroup.Key.AnimeId,
                languageGroup.Key.Kind,
                languageGroup.Key.Language!,
                languageGroup.Count(),
                languageGroup.Min(x => x.StreamIndex)))
            .ToListAsync(cancellationToken);

        var sidecarSubtitles = await (
            from track in db.SubtitleTracks.AsNoTracking()
            join episode in db.Episodes.AsNoTracking() on track.EpisodeId equals episode.Id
            group track by new { episode.AnimeId, track.Language } into languageGroup
            select new LanguageRow(
                languageGroup.Key.AnimeId,
                MediaStreamKind.Subtitle,
                languageGroup.Key.Language,
                languageGroup.Count(),
                int.MaxValue))
            .ToListAsync(cancellationToken);

        var languages = streams
            .Concat(sidecarSubtitles)
            .ToLookup(x => (x.AnimeId, x.Kind));

        return
        [
            .. titles.Select(title => BuildCard(
                title,
                episodes[title.Id].ToArray(),
                progress,
                OrderLanguages(languages[(title.Id, MediaStreamKind.Audio)]),
                OrderLanguages(languages[(title.Id, MediaStreamKind.Subtitle)]),
                openRequests))
        ];
    }

    /// <summary>
    /// Picks the episode the card's play button opens. The anchor is the most recently updated
    /// meaningful progress row (watched, or resumable past <see cref="EpisodeProgressService.MinimumResumeMs"/>),
    /// the same anchor Continue Watching uses. An unfinished anchor is resumed. After a watched
    /// anchor the canonical <see cref="EpisodeSequence"/> neighbour is used when it is unwatched,
    /// otherwise the first unwatched regular episode after the anchor, then the first unwatched
    /// regular episode overall. With no anchor the first episode starts; with everything watched
    /// the first episode is offered again.
    /// </summary>
    public static (MediaBannerProgressState State, EpisodeOrderKey Next)? ResolveNext(
        IReadOnlyCollection<EpisodeOrderKey> playableEpisodes,
        IReadOnlyDictionary<Guid, EpisodeProgressState> progress)
    {
        if (playableEpisodes.Count == 0)
        {
            return null;
        }

        var ordered = playableEpisodes
            .OrderBy(x => x.SeasonNumber <= 0 ? 1 : 0)
            .ThenBy(x => x.SeasonNumber)
            .ThenBy(x => x.Number)
            .ThenBy(x => x.Id)
            .ToArray();
        var regular = ordered.Where(x => x.SeasonNumber > 0).ToArray();
        var candidates = regular.Length > 0 ? regular : ordered;

        bool IsWatched(Guid id) => progress.TryGetValue(id, out var row) && row.IsCompleted;

        var anchor = ordered
            .Where(episode =>
                progress.TryGetValue(episode.Id, out var row) &&
                (row.IsCompleted || row.PositionMs >= EpisodeProgressService.MinimumResumeMs))
            .OrderByDescending(episode => progress[episode.Id].UpdatedAt)
            .ThenBy(episode => episode.Id)
            .FirstOrDefault();

        if (anchor is null)
        {
            return (MediaBannerProgressState.NotStarted, candidates[0]);
        }

        if (!IsWatched(anchor.Id))
        {
            return (MediaBannerProgressState.InProgress, anchor);
        }

        var sequenceNext = EpisodeSequence.Resolve(playableEpisodes, anchor.Id).NextEpisodeId;
        var next = sequenceNext is { } nextId && !IsWatched(nextId)
            ? ordered.First(x => x.Id == nextId)
            : candidates
                  .SkipWhile(x => x.Id != anchor.Id)
                  .Skip(1)
                  .FirstOrDefault(x => !IsWatched(x.Id))
              ?? candidates.FirstOrDefault(x => !IsWatched(x.Id));

        return next is null
            ? (MediaBannerProgressState.Completed, candidates[0])
            : (MediaBannerProgressState.InProgress, next);
    }

    private static MediaBannerCardData BuildCard(
        TitleRow title,
        IReadOnlyList<EpisodeRow> episodes,
        IReadOnlyDictionary<Guid, EpisodeProgressState> progress,
        IReadOnlyList<string> audioLanguages,
        IReadOnlyList<string> subtitleLanguages,
        IReadOnlyDictionary<string, AcquisitionRequestStatus> openRequests)
    {
        var playable = episodes
            .Where(x => x.HasMedia)
            .Select(x => new EpisodeOrderKey(x.Id, x.SeasonNumber, x.Number))
            .ToArray();
        var localSeasons = playable
            .Where(x => x.SeasonNumber > 0)
            .Select(x => x.SeasonNumber)
            .Distinct()
            .Order()
            .ToArray();

        return new MediaBannerCardData(
            MediaBannerKind.Anime,
            title.Title,
            $"/Library/Anime/{title.Id}",
            AnimeArtworkStore.ResolveFanartUrl(title.Id, title.BannerImageUrl)
                ?? AnimeArtworkStore.ResolvePosterUrl(title.Id, title.CoverImageUrl),
            title.Status,
            title.Year,
            title.AverageScore,
            audioLanguages,
            subtitleLanguages,
            localSeasons.Length > 0 ? localSeasons.Length : null,
            BuildProgress(title, episodes, playable, localSeasons, progress),
            new MediaAvailabilityFacts(
                InLibrary: true,
                HasPlayableContent: playable.Length > 0,
                Request: title.Provider == AniListMetadataProvider.ProviderKey
                    && title.ExternalId is { } externalId
                    && openRequests.TryGetValue(externalId, out var requestStatus)
                        ? requestStatus
                        : null));
    }

    private static MediaBannerProgress? BuildProgress(
        TitleRow title,
        IReadOnlyList<EpisodeRow> episodes,
        IReadOnlyList<EpisodeOrderKey> playable,
        IReadOnlyList<int> localSeasons,
        IReadOnlyDictionary<Guid, EpisodeProgressState> progress)
    {
        if (ResolveNext(playable, progress) is not { } resolved)
        {
            return null;
        }

        // Regular episodes known to the library (with or without a file yet), one per number.
        var regular = episodes
            .Where(x => x.SeasonNumber > 0)
            .GroupBy(x => (x.SeasonNumber, x.Number))
            .Select(x => x.ToArray())
            .ToArray();
        var multiSeason = localSeasons.Count > 1;

        int? total = null;
        if (!multiSeason && localSeasons.Count == 1)
        {
            var season = localSeasons[0];
            var localMax = regular.Where(x => x[0].SeasonNumber == season).Max(x => x[0].Number);
            total = season == 1 && title.ProviderEpisodeCount is int providerCount
                ? Math.Max(providerCount, localMax)
                : localMax;
        }

        var unitCount = total ?? regular.Length;
        var watched = regular.Count(group => group.Any(x =>
            progress.TryGetValue(x.Id, out var row) && row.IsCompleted));
        int? percent = unitCount > 0
            ? Math.Clamp((int)Math.Round(watched * 100d / unitCount), 0, 100)
            : null;

        var next = resolved.Next;
        var special = next.SeasonNumber <= 0;
        return new MediaBannerProgress(
            resolved.State,
            MediaBannerUnit.Episode,
            next.Number,
            $"/Library/Episode/{next.Id}",
            special ? null : total,
            multiSeason && !special ? next.SeasonNumber : null,
            percent);
    }

    // Most common first; ties keep the files' own track order (sidecars after embedded tracks).
    private static IReadOnlyList<string> OrderLanguages(IEnumerable<LanguageRow> rows) =>
    [
        .. rows
            .Select(x => (Code: PlaybackLanguages.Normalize(x.Language), x.Count, x.FirstStreamIndex))
            .Where(x => x.Code is not null && x.Code != PlaybackLanguages.SubtitlesOff)
            .GroupBy(x => x.Code!, StringComparer.Ordinal)
            .OrderByDescending(x => x.Sum(row => row.Count))
            .ThenBy(x => x.Min(row => row.FirstStreamIndex))
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Key)
    ];

    private sealed record TitleRow(
        Guid Id,
        string Title,
        string? Status,
        int? Year,
        int? AverageScore,
        int? ProviderEpisodeCount,
        string? BannerImageUrl,
        string? CoverImageUrl,
        string? Provider,
        string? ExternalId);

    private sealed record EpisodeRow(
        Guid Id,
        Guid AnimeId,
        int SeasonNumber,
        int Number,
        bool HasMedia);

    private sealed record LanguageRow(
        Guid AnimeId,
        MediaStreamKind Kind,
        string Language,
        int Count,
        int FirstStreamIndex);
}

/// <summary>The canonical progress facts one media card needs for a single episode.</summary>
public sealed record EpisodeProgressState(
    Guid EpisodeId,
    long PositionMs,
    bool IsCompleted,
    DateTime UpdatedAt);
