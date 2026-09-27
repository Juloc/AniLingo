package de.juloc.jularr.core.api

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HttpJularrClientApiTest {
    @Test
    fun featureParserIncludesNativeSessionAuth() {
        val enabled = setOf(
            "library",
            "nativeSessionAuth",
            "nativePlayerBootstrap",
            "directPlayback",
            "playbackProgress",
            "httpRangeRequests",
            "mediaTrackMetadata",
            "normalizedLearningCues",
            "learningStateMutation",
            "liveMp4Fallback",
            "storageAvailability",
            "ownerWakeOnLan",
        )

        val parsed = ClientFeatureFlagParser.parse { name -> name in enabled }

        assertTrue(parsed.nativeSessionAuth)
        assertTrue(parsed.library)
        assertTrue(parsed.nativePlayerBootstrap)
        assertFalse(parsed.hlsFallback)
        assertFalse(parsed.playbackSessions)
    }

    @Test
    fun continueWatchingAndPlaybackHistoryDefaultToFalseOnAnOlderServer() {
        val enabled = setOf(
            "library",
            "nativeSessionAuth",
            "nativePlayerBootstrap",
            "directPlayback",
            "playbackProgress",
            "httpRangeRequests",
            "mediaTrackMetadata",
            "normalizedLearningCues",
            "learningStateMutation",
            "liveMp4Fallback",
            "storageAvailability",
            "ownerWakeOnLan",
        )

        val parsed = ClientFeatureFlagParser.parse { name -> name in enabled }

        assertFalse(parsed.continueWatching)
        assertFalse(parsed.playbackHistory)
    }

    @Test
    fun continueWatchingAndPlaybackHistoryParseWhenAdvertised() {
        val enabled = setOf("continueWatching", "playbackHistory")
        val parsed = ClientFeatureFlagParser.parse { name -> name in enabled }

        assertTrue(parsed.continueWatching)
        assertTrue(parsed.playbackHistory)
    }
}
