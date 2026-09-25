package de.juloc.anilingo.core.api

import org.json.JSONObject
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HttpAniLingoClientApiTest {
    @Test
    fun capabilitiesParseNativeSessionAuth() {
        val api = HttpAniLingoClientApi("https://anilingo.example")
        val parsed = api.parseCapabilities(capabilitiesJson(nativeSessionAuth = true))

        assertTrue(parsed.features.nativeSessionAuth)
    }

    @Test
    fun missingNativeSessionAuthIsBackwardCompatibleFalse() {
        val json = capabilitiesJson(nativeSessionAuth = null)
        val api = HttpAniLingoClientApi("https://anilingo.example")

        val parsed = api.parseCapabilities(json)

        assertFalse(parsed.features.nativeSessionAuth)
    }

    private fun capabilitiesJson(nativeSessionAuth: Boolean?): JSONObject {
        val features = JSONObject()
            .put("library", true)
            .put("nativePlayerBootstrap", true)
            .put("directPlayback", true)
            .put("playbackProgress", true)
            .put("httpRangeRequests", true)
            .put("mediaTrackMetadata", true)
            .put("normalizedLearningCues", true)
            .put("learningStateMutation", true)
            .put("liveMp4Fallback", true)
            .put("hlsFallback", false)
            .put("playbackSessions", false)
            .put("companionPairing", false)
            .put("companionControl", false)
            .put("storageAvailability", true)
            .put("ownerWakeOnLan", true)

        nativeSessionAuth?.let { features.put("nativeSessionAuth", it) }

        return JSONObject()
            .put("apiVersion", 1)
            .put("minimumSupportedApiVersion", 1)
            .put("serverVersion", "test")
            .put("features", features)
    }
}
