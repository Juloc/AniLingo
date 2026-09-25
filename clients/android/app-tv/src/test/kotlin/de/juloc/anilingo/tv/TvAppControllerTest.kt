package de.juloc.anilingo.tv

import de.juloc.anilingo.core.api.AniLingoClientApi
import de.juloc.anilingo.core.model.AnimeDetail
import de.juloc.anilingo.core.model.ClientAccount
import de.juloc.anilingo.core.model.ClientCapabilities
import de.juloc.anilingo.core.model.ClientFeatureFlags
import de.juloc.anilingo.core.model.ClientLibrary
import de.juloc.anilingo.core.model.ClientLogin
import de.juloc.anilingo.core.model.CueResponse
import de.juloc.anilingo.core.model.EpisodeDetail
import de.juloc.anilingo.core.model.EpisodeProgress
import de.juloc.anilingo.core.model.EpisodeProgressUpdate
import de.juloc.anilingo.core.model.MediaAvailability
import de.juloc.anilingo.core.model.PlayerBootstrap
import de.juloc.anilingo.core.model.RootAvailability
import de.juloc.anilingo.core.model.TermDetail
import de.juloc.anilingo.core.model.TermStateResult
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test
import kotlin.coroutines.startCoroutine

class TvAppControllerTest {
    @Test
    fun connectPersistsNormalizedOriginAndMovesToLogin() {
        val store = FakeOriginStore()
        val api = FakeApi()
        val controller = TvAppController(store) { api }

        val state = runSuspend {
            controller.connect("HTTPS://AniLingo.Example/")
        }

        assertEquals("https://anilingo.example", store.origin)
        assertEquals(TvRoute.Login, state.navigation.route)
        assertFalse(state.busy)
        assertNull(state.error)
    }

    @Test
    fun loginMovesToLibraryAndKeepsAccountProfile() {
        val store = FakeOriginStore("https://anilingo.example")
        val api = FakeApi()
        val controller = TvAppController(store) { api }

        runSuspend { controller.restoreConnection() }
        val state = runSuspend { controller.login("jessi", "password-password") }

        assertEquals(TvRoute.Library, state.navigation.route)
        assertEquals("profile", state.account?.profileId)
        assertEquals(1, state.library?.anime?.size)
    }

    @Test
    fun changingServerDropsAuthenticatedStateAndCanonicalOrigin() {
        val store = FakeOriginStore("https://anilingo.example")
        val api = FakeApi()
        val controller = TvAppController(store) { api }

        runSuspend { controller.restoreConnection() }
        runSuspend { controller.login("jessi", "password-password") }
        val state = controller.changeServer()

        assertNull(store.origin)
        assertEquals(TvRoute.Setup, state.navigation.route)
        assertNull(state.account)
        assertNull(state.library)
    }

    @Test
    fun failedLoginKeepsLoginScreenAndSurfacesError() {
        val store = FakeOriginStore("https://anilingo.example")
        val api = FakeApi(failLogin = true)
        val controller = TvAppController(store) { api }

        runSuspend { controller.restoreConnection() }
        val state = runSuspend { controller.login("jessi", "wrong-password") }

        assertEquals(TvRoute.Login, state.navigation.route)
        assertEquals("Invalid user name or password.", state.error)
        assertFalse(state.busy)
    }

    private class FakeOriginStore(
        override var origin: String? = null,
    ) : TvServerOriginStore {
        override fun clear() {
            origin = null
        }
    }

    private class FakeApi(
        private val failLogin: Boolean = false,
    ) : AniLingoClientApi {
        override suspend fun getCapabilities() = ClientCapabilities(
            apiVersion = 1,
            minimumSupportedApiVersion = 1,
            serverVersion = "test",
            features = ClientFeatureFlags(
                library = true,
                nativeSessionAuth = true,
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

        override suspend fun login(credentials: ClientLogin): ClientAccount {
            if (failLogin) {
                error("Invalid user name or password.")
            }
            return ClientAccount("profile", credentials.userName, "owner")
        }

        override suspend fun logout() = Unit

        override suspend fun getMe() = ClientAccount(
            "profile",
            "jessi",
            "owner",
        )

        override suspend fun getLibrary() = ClientLibrary(
            anime = listOf(
                de.juloc.anilingo.core.model.AnimeSummary(
                    id = "anime",
                    title = "Anime",
                    localTitle = "Anime",
                    nativeTitle = null,
                    coverImageUrl = null,
                    bannerImageUrl = null,
                    episodeCount = 1,
                    seasonCount = 1,
                    seasonYear = 2026,
                    format = "TV",
                ),
            ),
        )

        override suspend fun getAnime(animeId: String): AnimeDetail = error("unused")
        override suspend fun getEpisode(episodeId: String): EpisodeDetail = error("unused")
        override suspend fun getProgress(episodeId: String): EpisodeProgress = error("unused")
        override suspend fun setProgress(
            episodeId: String,
            update: EpisodeProgressUpdate,
        ): EpisodeProgress = error("unused")
        override suspend fun getPlayer(episodeId: String): PlayerBootstrap = error("unused")
        override suspend fun getCues(
            episodeId: String,
            trackId: String?,
            fromMs: Int?,
            toMs: Int?,
        ): CueResponse = error("unused")
        override suspend fun getMediaAvailability(
            mediaFileId: String,
            fresh: Boolean,
        ): MediaAvailability = error("unused")
        override suspend fun getTerm(termId: String): TermDetail = error("unused")
        override suspend fun setTermState(
            termId: String,
            state: String,
        ): TermStateResult = TermStateResult(termId, state)
        override suspend fun getRootAvailability(rootId: String): RootAvailability = error("unused")
        override suspend fun testRoot(rootId: String): RootAvailability = error("unused")
        override suspend fun wakeRoot(rootId: String): RootAvailability = error("unused")
    }

    private fun <T> runSuspend(block: suspend () -> T): T {
        var result: Result<T>? = null
        block.startCoroutine(
            object : kotlin.coroutines.Continuation<T> {
                override val context = kotlin.coroutines.EmptyCoroutineContext
                override fun resumeWith(value: Result<T>) {
                    result = value
                }
            },
        )
        return result!!.getOrThrow()
    }
}
