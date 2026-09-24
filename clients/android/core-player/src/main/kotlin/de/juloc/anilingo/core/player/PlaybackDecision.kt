package de.juloc.anilingo.core.player

import de.juloc.anilingo.core.model.ClientCapabilities
import de.juloc.anilingo.core.model.PlayerBootstrap

enum class PlaybackTransport {
    DIRECT,
    LIVE_MP4_FALLBACK,
    HLS_FALLBACK,
}

data class DevicePlaybackSupport(
    val directContainerAndCodecSupported: Boolean,
)

object PlaybackSelector {
    fun select(
        capabilities: ClientCapabilities,
        bootstrap: PlayerBootstrap,
        device: DevicePlaybackSupport,
    ): PlaybackTransport {
        if (capabilities.nativePlayback &&
            bootstrap.supportsRanges &&
            device.directContainerAndCodecSupported
        ) {
            return PlaybackTransport.DIRECT
        }

        if (capabilities.hlsFallback && bootstrap.fallback?.hlsMasterUrl != null) {
            return PlaybackTransport.HLS_FALLBACK
        }

        if (capabilities.liveMp4Fallback && bootstrap.fallback?.liveMp4Url != null) {
            return PlaybackTransport.LIVE_MP4_FALLBACK
        }

        throw IllegalStateException("No supported playback transport is available.")
    }
}
