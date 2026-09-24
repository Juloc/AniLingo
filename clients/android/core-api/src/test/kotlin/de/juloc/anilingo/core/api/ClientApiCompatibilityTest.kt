package de.juloc.anilingo.core.api

import de.juloc.anilingo.core.model.ClientCapabilities
import org.junit.Assert.assertEquals
import org.junit.Test

class ClientApiCompatibilityTest {
    @Test
    fun currentApiIsCompatible() {
        assertEquals(
            ApiCompatibility.Compatible,
            ClientApiCompatibility.evaluate(capabilities(apiVersion = 1, minimumClient = 1)),
        )
    }

    @Test
    fun newerRequiredClientStopsOldClient() {
        assertEquals(
            ApiCompatibility.ClientTooOld(2),
            ClientApiCompatibility.evaluate(capabilities(apiVersion = 2, minimumClient = 2)),
        )
    }

    @Test
    fun olderServerStopsNewClient() {
        assertEquals(
            ApiCompatibility.ServerTooOld(0),
            ClientApiCompatibility.evaluate(capabilities(apiVersion = 0, minimumClient = 0)),
        )
    }

    private fun capabilities(
        apiVersion: Int,
        minimumClient: Int,
    ) = ClientCapabilities(
        apiVersion = apiVersion,
        minimumClientApiVersion = minimumClient,
        serverVersion = "test",
        nativePlayback = true,
        liveMp4Fallback = true,
        hlsFallback = false,
        pairing = false,
        companionControl = false,
    )
}
