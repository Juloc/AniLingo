package de.juloc.anilingo.core.api

import de.juloc.anilingo.core.model.ClientCapabilities
import de.juloc.anilingo.core.model.ClientFeatureFlags
import org.junit.Assert.assertEquals
import org.junit.Test

class ClientApiCompatibilityTest {
    @Test
    fun currentApiIsCompatible() {
        assertEquals(
            ApiCompatibility.Compatible,
            ClientApiCompatibility.evaluate(capabilities(apiVersion = 1, minimumSupported = 1)),
        )
    }

    @Test
    fun newerRequiredClientStopsOldClient() {
        assertEquals(
            ApiCompatibility.ClientTooOld(2),
            ClientApiCompatibility.evaluate(capabilities(apiVersion = 2, minimumSupported = 2)),
        )
    }

    @Test
    fun olderServerStopsNewClient() {
        assertEquals(
            ApiCompatibility.ServerTooOld(0),
            ClientApiCompatibility.evaluate(capabilities(apiVersion = 0, minimumSupported = 0)),
        )
    }

    private fun capabilities(
        apiVersion: Int,
        minimumSupported: Int,
    ) = ClientCapabilities(
        apiVersion = apiVersion,
        minimumSupportedApiVersion = minimumSupported,
        serverVersion = "test",
        features = ClientFeatureFlags(
            library = true,
            nativePlayerBootstrap = true,
            directPlayback = true,
            playbackProgress = true,
            httpRangeRequests = true,
            mediaTrackMetadata = true,
            normalizedLearningCues = true,
            learningStateMutation = true,
            liveMp4Fallback = true,
            hlsFallback = false,
            playbackSessions = false,
            companionPairing = false,
            companionControl = false,
            storageAvailability = true,
            ownerWakeOnLan = true,
        ),
    )
}
