package de.juloc.anilingo.mobile

import de.juloc.anilingo.core.api.ApiCompatibility
import de.juloc.anilingo.core.api.ClientApiCompatibility
import de.juloc.anilingo.core.model.ClientCapabilities

data class MobileShellState(
    val webUrl: String,
    val activeEpisodeId: String? = null,
) {
    fun openNativeEpisode(episodeId: String): MobileShellState =
        copy(activeEpisodeId = episodeId)

    fun closeNativePlayer(): MobileShellState =
        copy(activeEpisodeId = null)
}

sealed interface MobileCompatibilityState {
    data object Compatible : MobileCompatibilityState
    data class UpdateRequired(val detail: String) : MobileCompatibilityState
}

object MobileCompatibilityGate {
    fun evaluate(capabilities: ClientCapabilities): MobileCompatibilityState =
        when (val compatibility = ClientApiCompatibility.evaluate(capabilities)) {
            ApiCompatibility.Compatible -> MobileCompatibilityState.Compatible
            is ApiCompatibility.ClientTooOld -> MobileCompatibilityState.UpdateRequired(
                "This AniLingo server requires client API ${compatibility.minimumSupportedApiVersion}.",
            )
            is ApiCompatibility.ServerTooOld -> MobileCompatibilityState.UpdateRequired(
                "This app requires AniLingo client API ${ClientApiCompatibilityVersion.current}, but the server exposes ${compatibility.serverApiVersion}.",
            )
        }

    private object ClientApiCompatibilityVersion {
        const val current = 1
    }
}

object PlaybackPositionPolicy {
    fun absolutePosition(
        playerPositionMs: Long,
        sourceOffsetMs: Long,
    ): Long = (sourceOffsetMs + playerPositionMs).coerceAtLeast(0)

    fun preserve(
        playerPositionMs: Long,
        sourceOffsetMs: Long,
        shouldPlay: Boolean,
    ): PreservedPlayback = PreservedPlayback(
        positionMs = absolutePosition(playerPositionMs, sourceOffsetMs),
        shouldPlay = shouldPlay,
    )
}
