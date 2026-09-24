package de.juloc.anilingo.core.model

data class ClientCapabilities(
    val apiVersion: Int,
    val minimumClientApiVersion: Int,
    val serverVersion: String,
    val nativePlayback: Boolean,
    val liveMp4Fallback: Boolean,
    val hlsFallback: Boolean,
    val pairing: Boolean,
    val companionControl: Boolean,
)

data class PlayerBootstrap(
    val episodeId: String,
    val animeId: String,
    val episodeTitle: String,
    val animeTitle: String,
    val durationMs: Long,
    val mediaFileId: String,
    val container: String?,
    val videoCodec: String?,
    val directContentUrl: String,
    val directContentType: String?,
    val supportsRanges: Boolean,
    val audioTracks: List<AudioTrack>,
    val subtitleTracks: List<SubtitleTrack>,
    val fallback: CompatibilityFallback?,
    val activeLearningTrackId: String?,
)

data class AudioTrack(
    val id: String,
    val language: String?,
    val title: String?,
    val codec: String?,
    val isDefault: Boolean,
)

data class SubtitleTrack(
    val id: String,
    val language: String?,
    val title: String?,
    val kind: SubtitleTrackKind,
    val isDefault: Boolean,
    val isLearningSource: Boolean,
)

enum class SubtitleTrackKind {
    TEXT,
    IMAGE,
}

data class SubtitleCue(
    val id: String,
    val startMs: Long,
    val endMs: Long,
    val text: String,
)

data class CompatibilityFallback(
    val liveMp4Url: String?,
    val hlsMasterUrl: String?,
)

data class TermSummary(
    val id: String,
    val surface: String,
    val reading: String?,
    val meaning: String?,
    val state: TermLearningState,
)

enum class TermLearningState {
    UNKNOWN,
    LEARNING,
    KNOWN,
}
