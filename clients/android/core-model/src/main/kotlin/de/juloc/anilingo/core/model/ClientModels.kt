package de.juloc.anilingo.core.model

data class ClientCapabilities(
    val apiVersion: Int,
    val minimumSupportedApiVersion: Int,
    val serverVersion: String,
    val features: ClientFeatureFlags,
)

data class ClientFeatureFlags(
    val library: Boolean,
    val nativePlayerBootstrap: Boolean,
    val directPlayback: Boolean,
    val playbackProgress: Boolean,
    val httpRangeRequests: Boolean,
    val mediaTrackMetadata: Boolean,
    val normalizedLearningCues: Boolean,
    val learningStateMutation: Boolean,
    val liveMp4Fallback: Boolean,
    val hlsFallback: Boolean,
    val playbackSessions: Boolean,
    val companionPairing: Boolean,
    val companionControl: Boolean,
    val storageAvailability: Boolean,
    val ownerWakeOnLan: Boolean,
)

data class ClientAccount(
    val profileId: String,
    val userName: String?,
    val role: String,
)

data class ClientLibrary(
    val anime: List<AnimeSummary>,
)

data class AnimeSummary(
    val id: String,
    val title: String,
    val localTitle: String,
    val nativeTitle: String?,
    val coverImageUrl: String?,
    val bannerImageUrl: String?,
    val episodeCount: Int,
    val seasonCount: Int,
    val seasonYear: Int?,
    val format: String?,
)

data class AnimeDetail(
    val id: String,
    val title: String,
    val localTitle: String,
    val nativeTitle: String?,
    val description: String?,
    val coverImageUrl: String?,
    val bannerImageUrl: String?,
    val seasonYear: Int?,
    val format: String?,
    val seasons: List<Season>,
)

data class Season(
    val number: Int,
    val episodes: List<EpisodeSummary>,
)

data class EpisodeSummary(
    val id: String,
    val seasonNumber: Int,
    val number: Int,
    val title: String,
    val hasMedia: Boolean,
    val hasJapaneseLearningSubtitle: Boolean,
)

data class EpisodeDetail(
    val id: String,
    val animeId: String,
    val animeTitle: String,
    val title: String,
    val seasonNumber: Int,
    val number: Int,
    val hasMedia: Boolean,
    val activeLearningSubtitleTrackId: String?,
    val learningCueCount: Int,
    val learning: LearningCoverage,
)

data class LearningCoverage(
    val totalTerms: Int,
    val knownTerms: Int,
    val learningTerms: Int,
    val newTerms: Int,
)

data class EpisodeProgressUpdate(
    val positionMs: Long,
    val durationMs: Long?,
    val completed: Boolean,
)

data class EpisodeProgress(
    val positionMs: Long,
    val durationMs: Long?,
    val percent: Int,
    val isCompleted: Boolean,
    val updatedAtUtc: String?,
)

data class PlayerBootstrap(
    val apiVersion: Int,
    val episode: PlayerEpisode,
    val media: PlayerMedia?,
    val audioTracks: List<MediaTrack>,
    val subtitleTracks: List<MediaTrack>,
    val learningSubtitles: List<LearningSubtitle>,
    val activeLearningSubtitleTrackId: String?,
    val defaultAudioTrackId: String?,
    val defaultSubtitleTrackId: String?,
    val fallback: CompatibilityFallback,
)

data class PlayerEpisode(
    val id: String,
    val animeId: String,
    val animeTitle: String,
    val title: String,
    val seasonNumber: Int,
    val number: Int,
)

data class PlayerMedia(
    val mediaFileId: String,
    val fileName: String,
    val contentType: String,
    val sizeBytes: Long?,
    val durationMs: Long?,
    val videoCodec: String?,
    val pixelFormat: String?,
    val audioCodec: String?,
    val directContentUrl: String,
    val supportsRangeRequests: Boolean,
    val device: PlaybackOption,
    val server: PlaybackOption,
    val availability: MediaAvailability,
)

data class PlaybackOption(
    val availability: String,
    val message: String,
    val usesLiveStream: Boolean,
)

data class MediaAvailability(
    val state: String,
    val retryable: Boolean,
    val retryAfterMs: Int,
    val canWake: Boolean,
    val rootId: String?,
    val availabilityUrl: String,
    val wakeUrl: String?,
) {
    val isAvailable: Boolean
        get() = state == "available"
}

data class RootAvailability(
    val rootId: String,
    val state: String,
    val retryable: Boolean,
    val checkedAtUtc: String,
    val lastAvailableAtUtc: String?,
    val wakeConfigured: Boolean,
    val diagnosticCode: String?,
)

data class MediaTrack(
    val id: String,
    val streamIndex: Int,
    val kind: String,
    val codec: String?,
    val language: String?,
    val title: String?,
    val isDefault: Boolean,
    val isForced: Boolean,
    val isText: Boolean,
)

data class LearningSubtitle(
    val trackId: String,
    val language: String,
    val format: String,
    val isActive: Boolean,
    val cuesUrl: String,
)

data class CompatibilityFallback(
    val available: Boolean,
    val kind: String?,
    val seekableWithinStream: Boolean,
    val canRestartAtPosition: Boolean,
    val url: String?,
)

data class CueResponse(
    val trackId: String?,
    val fromMs: Int?,
    val toMs: Int?,
    val cues: List<SubtitleCue>,
)

data class SubtitleCue(
    val id: Long,
    val startMs: Int,
    val endMs: Int,
    val text: String,
    val tokens: List<CueToken>,
)

data class CueToken(
    val surface: String,
    val termId: String?,
    val canonical: String?,
    val reading: String?,
    val meaning: String?,
    val state: String,
)

data class TermDetail(
    val id: String,
    val canonical: String,
    val reading: String?,
    val meaning: String?,
    val state: String,
)

data class TermStateUpdate(
    val state: String,
)

data class TermStateResult(
    val termId: String,
    val state: String,
)
