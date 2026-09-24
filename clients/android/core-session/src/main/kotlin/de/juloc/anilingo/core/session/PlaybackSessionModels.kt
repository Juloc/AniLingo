package de.juloc.anilingo.core.session

data class PlaybackSessionSnapshot(
    val sessionId: String,
    val episodeId: String,
    val positionMs: Long,
    val durationMs: Long,
    val isPlaying: Boolean,
    val playbackRate: Float,
    val audioTrackId: String?,
    val subtitleTrackId: String?,
    val currentCueId: String?,
    val currentCueText: String?,
    val selectedTermId: String?,
    val revision: Long,
    val updatedAtUtc: String,
    val controllerClientId: String?,
)

data class PlaybackCommand(
    val commandId: String,
    val sessionId: String,
    val expectedRevision: Long,
    val type: PlaybackCommandType,
    val payload: Map<String, String> = emptyMap(),
    val sentAtUtc: String,
)

enum class PlaybackCommandType {
    PLAY_PAUSE,
    SEEK_BACK_10,
    SEEK_FORWARD_10,
    SEEK_TO,
    SELECT_AUDIO_TRACK,
    SELECT_SUBTITLE_TRACK,
    REPEAT_CURRENT_CUE,
    LEARN_CURRENT_CUE,
    OPEN_WORD,
    MARK_KNOWN,
    ADD_TO_LEARNING,
    OPEN_COMPANION,
    CLOSE_OVERLAY,
    EXIT_PLAYER,
}
