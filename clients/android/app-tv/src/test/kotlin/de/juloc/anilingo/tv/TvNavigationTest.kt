package de.juloc.anilingo.tv

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class TvNavigationTest {
    @Test
    fun firstLaunchWithoutServerStartsSetup() {
        assertEquals(
            TvRoute.Setup,
            TvNavigation.initial(hasServerOrigin = false).route,
        )
    }

    @Test
    fun savedServerStartsAtLogin() {
        assertEquals(
            TvRoute.Login,
            TvNavigation.initial(hasServerOrigin = true).route,
        )
    }

    @Test
    fun libraryAnimePlayerBackStackIsRemoteFriendly() {
        var state = TvNavigationState(TvRoute.Library)
        state = TvNavigation.openAnime(state, "anime")
        state = TvNavigation.openPlayer(state, "episode", "anime")

        state = TvNavigation.back(state)!!
        assertEquals(TvRoute.Anime("anime"), state.route)

        state = TvNavigation.back(state)!!
        assertEquals(TvRoute.Library, state.route)

        assertNull(TvNavigation.back(state))
    }

    @Test
    fun signOutDropsProtectedBackStack() {
        var state = TvNavigationState(TvRoute.Library)
        state = TvNavigation.openAnime(state, "anime")
        state = TvNavigation.signOut(state)

        assertEquals(TvRoute.Login, state.route)
        assertEquals(emptyList<TvRoute>(), state.previous)
    }

    @Test
    fun changingServerDropsAuthenticatedNavigation() {
        val state = TvNavigation.changeServer()

        assertEquals(TvRoute.Setup, state.route)
        assertNull(TvNavigation.back(state))
    }
}
