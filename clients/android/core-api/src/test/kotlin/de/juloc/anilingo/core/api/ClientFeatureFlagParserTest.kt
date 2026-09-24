package de.juloc.anilingo.core.api

import org.junit.Assert.assertTrue
import org.junit.Test

class ClientFeatureFlagParserTest {
    @Test
    fun nativeSessionAuthIsReadFromServerFeatureMap() {
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

        val parsed = ClientFeatureFlagParser.parse { name ->
            name in enabled
        }

        assertTrue(parsed.nativeSessionAuth)
        assertTrue(parsed.library)
        assertTrue(parsed.nativePlayerBootstrap)
    }
}
