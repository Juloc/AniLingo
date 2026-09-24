package de.juloc.anilingo.mobile

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ServerOriginTest {
    private val origin = ServerOrigin.parse("https://anilingo.example:8443").getOrThrow()

    @Test
    fun sameOriginNavigationStaysInWebView() {
        assertEquals(
            WebNavigationDecision.AllowInWebView,
            WebNavigationPolicy.decide(
                origin,
                "https://anilingo.example:8443/Library",
            ),
        )
    }

    @Test
    fun externalLinkLeavesWebView() {
        val decision = WebNavigationPolicy.decide(
            origin,
            "https://example.org/help",
        )
        assertTrue(decision is WebNavigationDecision.OpenExternal)
    }

    @Test
    fun canonicalEpisodeRouteOpensNativePlayerWithExactId() {
        val id = "7a3c4f54-66a8-4acd-98aa-2fcd597f9466"
        assertEquals(
            WebNavigationDecision.OpenNativeEpisode(id),
            WebNavigationPolicy.decide(
                origin,
                "https://anilingo.example:8443/Library/Episode/$id",
            ),
        )
    }

    @Test
    fun lookalikeEpisodeRouteIsNotIntercepted() {
        assertEquals(
            WebNavigationDecision.AllowInWebView,
            WebNavigationPolicy.decide(
                origin,
                "https://anilingo.example:8443/Library/Episodes/not-an-id",
            ),
        )
    }

    @Test
    fun originRejectsApplicationPaths() {
        assertTrue(ServerOrigin.parse("https://anilingo.example/app").isFailure)
    }
}
