using Jularr.Web.Data;
using Jularr.Web.Features.Artwork;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Instance;
using Jularr.Web.Features.Learning;
using Jularr.Web.Features.Learning.LanguageAssistance;
using Jularr.Web.Features.MediaSegments;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Playback;
using Jularr.Web.Features.Progress;
using Jularr.Web.Features.Storage;
using Jularr.Web.Features.Subtitles;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.ClientApi;

public sealed class ClientApiService(
    AppDbContext db,
    PlaybackService playbackService,
    LearningService learningService,
    MediaAvailabilityService mediaAvailability,
    EpisodeProgressService progressService,
    CurrentAccountContext currentAccount,
    MediaSegmentService mediaSegments,
    IInstanceModuleService? instanceModules = null)
{
    public async Task<ClientLibraryResponse> GetLibraryAsync(
        CancellationToken cancellationToken)
    {
        var animeRows = await (
            from anime in db.Anime.AsNoTracking()
            join metadataValue in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadataValue.AnimeId into metadataRows
            from metadata in metadataRows.DefaultIfEmpty()
            orderby metadata != null ? metadata.PreferredTitle : anime.Title
            select new
            {
                anime.Id,
                LocalTitle = anime.Title,
                PreferredTitle = metadata == null ? null : metadata.PreferredTitle,
                NativeTitle = metadata == null ? null : metadata.NativeTitle,
                CoverImageUrl = metadata == null ? null : metadata.CoverImageUrl,
                BannerImageUrl = metadata == null ? null : metadata.BannerImageUrl,
                SeasonYear = metadata == null ? null : metadata.SeasonYear,
                Format = metadata == null ? null : metadata.Format
            })
            .ToListAsync(cancellationToken);

        var episodeCounts = await db.Episodes
            .AsNoTracking()
            .GroupBy(x => x.AnimeId)
            .Select(group => new
            {
                AnimeId = group.Key,
                EpisodeCount = group.Count(),
                SeasonCount = group.Select(x => x.SeasonNumber).Distinct().Count()
            })
            .ToDictionaryAsync(x => x.AnimeId, cancellationToken);

        return new ClientLibraryResponse(
            animeRows.Select(row =>
            {
                episodeCounts.TryGetValue(row.Id, out var counts);

                return new ClientAnimeSummary(
                    row.Id,
                    row.PreferredTitle ?? row.LocalTitle,
                    row.LocalTitle,
                    row.NativeTitle,
                    AnimeArtworkStore.ResolvePosterUrl(row.Id, row.CoverImageUrl),
                    AnimeArtworkStore.ResolveFanartUrl(row.Id, row.BannerImageUrl),
                    counts?.EpisodeCount ?? 0,
                    counts?.SeasonCount ?? 0,
                    row.SeasonYear,
                    row.Format);
            }).ToArray());
    }

    public async Task<ClientAnimeDetail?> GetAnimeAsync(
        Guid animeId,
        CancellationToken cancellationToken)
    {
        var row = await (
            from anime in db.Anime.AsNoTracking()
            join metadataValue in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadataValue.AnimeId into metadataRows
            from metadata in metadataRows.DefaultIfEmpty()
            where anime.Id == animeId
            select new
            {
                anime.Id,
                LocalTitle = anime.Title,
                PreferredTitle = metadata == null ? null : metadata.PreferredTitle,
                NativeTitle = metadata == null ? null : metadata.NativeTitle,
                Description = metadata == null ? null : metadata.Description,
                CoverImageUrl = metadata == null ? null : metadata.CoverImageUrl,
                BannerImageUrl = metadata == null ? null : metadata.BannerImageUrl,
                SeasonYear = metadata == null ? null : metadata.SeasonYear,
                Format = metadata == null ? null : metadata.Format
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var learningEnabled = await IsLearningEnabledAsync(cancellationToken);

        var episodeRows = await db.Episodes
            .AsNoTracking()
            .Where(x => x.AnimeId == animeId)
            .OrderBy(x => x.SeasonNumber)
            .ThenBy(x => x.Number)
            .Select(x => new
            {
                x.Id,
                x.SeasonNumber,
                x.Number,
                x.Title,
                HasMedia = db.MediaFiles.Any(media => media.EpisodeId == x.Id),
                HasJapaneseLearningSubtitle = db.SubtitleTracks.Any(
                    track => track.EpisodeId == x.Id && track.Language == "ja")
            })
            .ToListAsync(cancellationToken);

        var seasons = episodeRows
            .GroupBy(x => x.SeasonNumber)
            .Select(group => new ClientSeason(
                group.Key,
                group.Select(episode => new ClientEpisodeSummary(
                    episode.Id,
                    episode.SeasonNumber,
                    episode.Number,
                    episode.Title,
                    episode.HasMedia,
                    learningEnabled && episode.HasJapaneseLearningSubtitle))
                    .ToArray()))
            .ToArray();

        return new ClientAnimeDetail(
            row.Id,
            row.PreferredTitle ?? row.LocalTitle,
            row.LocalTitle,
            row.NativeTitle,
            row.Description,
            AnimeArtworkStore.ResolvePosterUrl(row.Id, row.CoverImageUrl),
            AnimeArtworkStore.ResolveFanartUrl(row.Id, row.BannerImageUrl),
            row.SeasonYear,
            row.Format,
            seasons);
    }

    public async Task<ClientEpisodeDetail?> GetEpisodeAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var row = await (
            from episode in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            join metadataValue in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadataValue.AnimeId into metadataRows
            from metadata in metadataRows.DefaultIfEmpty()
            where episode.Id == episodeId
            select new
            {
                episode.Id,
                episode.AnimeId,
                AnimeTitle = metadata == null ? anime.Title : metadata.PreferredTitle,
                episode.Title,
                episode.SeasonNumber,
                episode.Number
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var hasMedia = await db.MediaFiles
            .AsNoTracking()
            .AnyAsync(x => x.EpisodeId == episodeId, cancellationToken);

        Guid? activeTrackId = null;
        var cueCount = 0;
        var termStates = new List<UserTermState?>();

        if (await IsLearningEnabledAsync(cancellationToken))
        {
            activeTrackId = await db.SubtitleTracks
                .AsNoTracking()
                .Where(x => x.EpisodeId == episodeId && x.Language == "ja")
                .OrderByDescending(x => x.ImportedAt)
                .ThenBy(x => x.Id)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeTrackId is { } trackId)
            {
                cueCount = await db.SubtitleCues
                    .AsNoTracking()
                    .CountAsync(
                        x => x.SubtitleTrackId == trackId,
                        cancellationToken);
            }

            termStates = await (
                from episodeTerm in db.EpisodeTerms.AsNoTracking()
                join stateValue in LearningQueries.TermStates(db, currentAccount.ProfileId)
                    on episodeTerm.TermId equals stateValue.TermId into states
                from state in states.DefaultIfEmpty()
                where episodeTerm.EpisodeId == episodeId
                select state == null ? (UserTermState?)null : state.State)
                .ToListAsync(cancellationToken);
        }

        var known = termStates.Count(x => x == UserTermState.Known);
        var learning = termStates.Count(x => x == UserTermState.Learning);

        return new ClientEpisodeDetail(
            row.Id,
            row.AnimeId,
            row.AnimeTitle,
            row.Title,
            row.SeasonNumber,
            row.Number,
            hasMedia,
            activeTrackId,
            cueCount,
            new ClientLearningCoverage(
                termStates.Count,
                known,
                learning,
                termStates.Count - known - learning));
    }

    public async Task<ClientPlayerBootstrap?> GetPlayerAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var episode = await (
            from localEpisode in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on localEpisode.AnimeId equals anime.Id
            join metadataValue in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadataValue.AnimeId into metadataRows
            from metadata in metadataRows.DefaultIfEmpty()
            where localEpisode.Id == episodeId
            select new ClientPlayerEpisode(
                localEpisode.Id,
                anime.Id,
                metadata == null ? anime.Title : metadata.PreferredTitle,
                localEpisode.Title,
                localEpisode.SeasonNumber,
                localEpisode.Number))
            .SingleOrDefaultAsync(cancellationToken);

        if (episode is null)
        {
            return null;
        }

        var media = await playbackService.GetMediaAsync(
            episodeId,
            cancellationToken);

        var learningTrackRows = new List<SubtitleTrack>();
        if (await IsLearningEnabledAsync(cancellationToken))
        {
            learningTrackRows = await db.SubtitleTracks
                .AsNoTracking()
                .Where(x => x.EpisodeId == episodeId && x.Language == "ja")
                .OrderByDescending(x => x.ImportedAt)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);
        }

        var activeLearningTrackId = learningTrackRows
            .Select(x => (Guid?)x.Id)
            .FirstOrDefault();

        var learningTracks = learningTrackRows
            .Select(x => new ClientLearningSubtitle(
                x.Id,
                x.Language,
                x.Format,
                x.Id == activeLearningTrackId,
                $"{ClientApiRoutes.Cues(episodeId)}?trackId={x.Id:D}"))
            .ToArray();

        var navigation = await mediaSegments.GetPlayerNavigationAsync(
            episodeId,
            media,
            cancellationToken);
        var segments = ClientApiMappings.ToClientSegments(navigation.Segments);
        var trickplay = ClientApiMappings.ToClientTrickplay(episodeId, navigation.Trickplay);

        var preferences = await progressService.GetPreferencesAsync(cancellationToken);
        var learningSettings = await new LearningConfigurationStore(db, instanceModules).ResolveAsync(
            currentAccount.ProfileId,
            new LearningScopeContext(
                LearningMediaType.Anime,
                WorkKey: episode.AnimeId.ToString(),
                ContentKey: episodeId.ToString()),
            cancellationToken);
        // Learning is opt-in: the learning subtitle is only a default when the
        // profile enabled player language tools.
        var learningSubtitleDefault = activeLearningTrackId is not null
            && LanguageAssistanceAvailability.AllowsLearningSubtitle(learningSettings);
        var controls = new ClientPlayerControls(
            PlaybackPreferenceRules.Speeds,
            PlaybackQuality.Names);

        if (media is null)
        {
            var noMediaSubtitle = PlaybackTrackSelection.ResolveSubtitle(
                null,
                learningSubtitleDefault,
                preferences.PreferredSubtitleLanguage);

            return new ClientPlayerBootstrap(
                ClientApiContract.ApiVersion,
                episode,
                null,
                [],
                [],
                learningTracks,
                activeLearningTrackId,
                null,
                null,
                new ClientCompatibilityFallback(
                    false,
                    null,
                    false,
                    false,
                    null),
                new ClientPlayerDefaults(
                    null,
                    noMediaSubtitle.ModeName,
                    noMediaSubtitle.TrackId,
                    preferences.DefaultPlaybackSpeed,
                    ClientApiMappings.ToClientPreferences(preferences)),
                controls,
                segments,
                trickplay);
        }

        var tracks = media.Tracks ?? [];
        var audioTracks = tracks
            .Where(x => x.Kind == PlaybackTrackKind.Audio)
            .Select(ClientApiMappings.ToClientTrack)
            .ToArray();
        var subtitleTracks = tracks
            .Where(x => x.Kind == PlaybackTrackKind.Subtitle)
            .Select(ClientApiMappings.ToClientTrack)
            .ToArray();

        var defaultAudio = PlaybackTrackSelection.DefaultAudio(tracks);
        var defaultSubtitle = subtitleTracks.FirstOrDefault(x => x.IsDefault);
        var preferredAudio = PlaybackTrackSelection.ResolveAudio(
            tracks,
            preferences.PreferredAudioLanguage);
        var preferredSubtitle = PlaybackTrackSelection.ResolveSubtitle(
            tracks,
            learningSubtitleDefault,
            preferences.PreferredSubtitleLanguage);

        var fallbackAvailable = media.Server.IsReady;
        var storage = media.Storage
            ?? await mediaAvailability.CheckMediaAsync(
                media.MediaFileId,
                force: false,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Playback media exists without an availability record.");

        var clientMedia = new ClientPlayerMedia(
            media.MediaFileId,
            media.FileName,
            media.ContentType,
            media.SizeBytes,
            media.DurationSeconds is > 0
                ? (long?)Math.Round(media.DurationSeconds.Value * 1000)
                : null,
            media.VideoCodec,
            media.PixelFormat,
            media.AudioCodec,
            ClientApiRoutes.DirectContent(media.MediaFileId),
            true,
            ClientApiMappings.ToClientOption(media.Device),
            ClientApiMappings.ToClientOption(media.Server),
            ClientApiMappings.ToClientAvailability(
                storage,
                currentAccount.IsOwner));

        return new ClientPlayerBootstrap(
            ClientApiContract.ApiVersion,
            episode,
            clientMedia,
            audioTracks,
            subtitleTracks,
            learningTracks,
            activeLearningTrackId,
            defaultAudio is null ? null : PlaybackTrackIds.Format(defaultAudio.StreamIndex),
            defaultSubtitle?.Id,
            new ClientCompatibilityFallback(
                fallbackAvailable,
                fallbackAvailable ? "hls" : null,
                fallbackAvailable,
                fallbackAvailable,
                fallbackAvailable ? ClientApiRoutes.Hls(episodeId) : null),
            new ClientPlayerDefaults(
                preferredAudio is null ? null : PlaybackTrackIds.Format(preferredAudio.StreamIndex),
                preferredSubtitle.ModeName,
                preferredSubtitle.TrackId,
                preferences.DefaultPlaybackSpeed,
                ClientApiMappings.ToClientPreferences(preferences)),
            controls,
            segments,
            trickplay);
    }

    public async Task<ClientSegmentDescriptor?> GetSegmentsAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        if (!await db.Episodes.AsNoTracking().AnyAsync(x => x.Id == episodeId, cancellationToken))
        {
            return null;
        }

        return ClientApiMappings.ToClientSegments(
            await mediaSegments.GetSegmentsAsync(episodeId, cancellationToken));
    }

    public async Task<ClientTrickplayDescriptor?> GetTrickplayAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        if (!await db.Episodes.AsNoTracking().AnyAsync(x => x.Id == episodeId, cancellationToken))
        {
            return null;
        }

        return ClientApiMappings.ToClientTrickplay(
            episodeId,
            await mediaSegments.GetTrickplayAsync(episodeId, cancellationToken));
    }

    public async Task<ClientCueResponse?> GetCuesAsync(
        Guid episodeId,
        Guid? trackId,
        int? fromMs,
        int? toMs,
        CancellationToken cancellationToken)
    {
        var exists = await db.Episodes
            .AsNoTracking()
            .AnyAsync(x => x.Id == episodeId, cancellationToken);

        if (!exists)
        {
            return null;
        }

        if (!await IsLearningEnabledAsync(cancellationToken))
        {
            return new ClientCueResponse(
                null,
                fromMs,
                toMs,
                []);
        }

        var cueSet = await playbackService.GetCueSetAsync(
            episodeId,
            trackId,
            fromMs,
            toMs,
            cancellationToken);

        return new ClientCueResponse(
            cueSet.TrackId,
            fromMs,
            toMs,
            cueSet.Cues.Select(ClientApiMappings.ToClientCue).ToArray());
    }

    public async Task<ClientTermDetail?> GetTermAsync(
        Guid termId,
        CancellationToken cancellationToken)
    {
        if (!await IsLearningEnabledAsync(cancellationToken))
        {
            return null;
        }

        var row = await (
            from term in db.Terms.AsNoTracking()
            join stateValue in LearningQueries.TermStates(db, currentAccount.ProfileId)
                on term.Id equals stateValue.TermId into states
            from state in states.DefaultIfEmpty()
            where term.Id == termId
            select new
            {
                term.Id,
                term.Canonical,
                term.Reading,
                term.Meaning,
                State = state == null ? (UserTermState?)null : state.State
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ClientTermDetail(
                row.Id,
                row.Canonical,
                row.Reading,
                row.Meaning,
                ClientApiMappings.StateName(row.State));
    }

    public async Task<ClientTermStateResult?> SetTermStateAsync(
        Guid termId,
        UserTermState state,
        CancellationToken cancellationToken)
    {
        if (!await IsLearningEnabledAsync(cancellationToken))
        {
            return null;
        }

        var exists = await db.Terms
            .AsNoTracking()
            .AnyAsync(x => x.Id == termId, cancellationToken);

        if (!exists)
        {
            return null;
        }

        await learningService.SetStateAsync(termId, state, cancellationToken);
        return new ClientTermStateResult(
            termId,
            ClientApiMappings.StateName(state));
    }

    private async Task<bool> IsLearningEnabledAsync(
        CancellationToken cancellationToken) =>
        instanceModules is null
        || await instanceModules.IsEnabledAsync(
            InstanceModule.Learning,
            cancellationToken);
}
