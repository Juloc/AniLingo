package de.juloc.anilingo.core.player

import de.juloc.anilingo.core.model.ClientCapabilities
import de.juloc.anilingo.core.model.CompatibilityFallback
import de.juloc.anilingo.core.model.PlayerBootstrap
import org.junit.Assert.assertEquals
import org.junit.Test

class PlaybackSelectorTest {
    @Test
    fun directPlaybackWinsWhenDeviceSupportsOriginal() {
        assertEquals(
            PlaybackTransport.DIRECT,
            PlaybackSelector.select(
                capabilities(hls = true, liveMp4 = true),
                bootstrap(),
                DevicePlaybackSupport(directContainerAndCodecSupported = true),
            ),
        )
    }

    @Test
    fun hlsWinsOverLiveMp4WhenDirectIsUnsupported() {
        assertEquals(
            PlaybackTransport.HLS_FALLBACK,
            PlaybackSelector.select(
                capabilities(hls = true, liveMp4 = true),
                bootstrap(),
                DevicePlaybackSupport(directContainerAndCodecSupported = false),
            ),
        )
    }

    @Test
    fun liveMp4RemainsSupportedUntilHlsExists() {
        assertEquals(
            PlaybackTransport.LIVE_MP4_FALLBACK,
            PlaybackSelector.select(
                capabilities(hls = false, liveMp4 = true),
                bootstrap(),
                DevicePlaybackSupport(directContainerAndCodecSupported = false),
            ),
        )
    }

    private fun capabilities(
        hls: Boolean,
        liveMp4: Boolean,
    ) = ClientCapabilities(
        apiVersion = 1,
        minimumClientApiVersion = 1,
        serverVersion = "test",
        nativePlayback = true,
        liveMp4Fallback = liveMp4,
        hlsFallback = hls,
        pairing = false,
        companionControl = false,
    )

    private fun bootstrap() = PlayerBootstrap(
        episodeId = "episode",
        animeId = "anime",
        episodeTitle = "Episode",
        animeTitle = "Anime",
        durationMs = 1000,
        mediaFileId = "media",
        container = "mkv",
        videoCodec = "hevc",
        directContentUrl = "/direct",
        directContentType = "video/x-matroska",
        supportsRanges = true,
        audioTracks = emptyList(),
        subtitleTracks = emptyList(),
        fallback = CompatibilityFallback(
            liveMp4Url = "/fallback",
            hlsMasterUrl = "/master.m3u8",
        ),
        activeLearningTrackId = null,
    )
}
