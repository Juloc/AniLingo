package de.juloc.anilingo.tv

import de.juloc.anilingo.core.api.AniLingoClientApi
import de.juloc.anilingo.core.model.AnimeDetail
import de.juloc.anilingo.core.model.ClientAccount
import de.juloc.anilingo.core.model.ClientCapabilities
import de.juloc.anilingo.core.model.ClientLibrary
import de.juloc.anilingo.core.model.CueResponse

data class TvAppSnapshot(
    val navigation: TvNavigationState,
    val capabilities: ClientCapabilities? = null,
    val account: ClientAccount? = null,
    val library: ClientLibrary? = null,
    val anime: AnimeDetail? = null,
    val episode: TvEpisodeBundle? = null,
    val storageDecision: TvStorageDecision? = null,
    val busy: Boolean = false,
    val error: String? = null,
)

class TvAppController(
    private val settings: TvServerOriginStore,
    apiFactory: (String) -> AniLingoClientApi,
) {
    private val flow = TvClientFlow(apiFactory)

    var snapshot = TvAppSnapshot(
        navigation = TvNavigation.initial(settings.origin != null),
    )
        private set

    suspend fun connect(rawOrigin: String): TvAppSnapshot =
        runBusy {
            val capabilities = flow.connect(rawOrigin)
            val origin = requireNotNull(flow.origin)
            settings.origin = origin

            copy(
                navigation = TvNavigation.connected(navigation),
                capabilities = capabilities,
                error = null,
            )
        }

    suspend fun login(
        userName: String,
        password: String,
    ): TvAppSnapshot =
        runBusy {
            ensureConnected()
            val signedIn = flow.login(userName, password)
            copy(
                navigation = TvNavigation.signedIn(navigation),
                account = signedIn.account,
                library = signedIn.library,
                anime = null,
                episode = null,
                storageDecision = null,
                error = null,
            )
        }

    suspend fun restoreConnection(): TvAppSnapshot =
        runBusy {
            val origin = settings.origin
                ?: return@runBusy copy(
                    navigation = TvNavigation.changeServer(),
                    error = null,
                )
            val capabilities = flow.connect(origin)
            copy(
                navigation = TvNavigation.connected(navigation),
                capabilities = capabilities,
                error = null,
            )
        }

    suspend fun refreshLibrary(): TvAppSnapshot =
        runBusy {
            copy(
                library = flow.refreshLibrary(),
                error = null,
            )
        }

    suspend fun openAnime(animeId: String): TvAppSnapshot =
        runBusy {
            val loaded = flow.loadAnime(animeId)
            copy(
                navigation = TvNavigation.openAnime(navigation, animeId),
                anime = loaded,
                episode = null,
                storageDecision = null,
                error = null,
            )
        }

    suspend fun openEpisode(
        episodeId: String,
        animeId: String,
    ): TvAppSnapshot =
        runBusy {
            val bundle = flow.loadEpisode(episodeId)
            val decision = bundle.bootstrap.media?.availability?.let {
                TvStorageRecoveryPolicy.decide(it, elapsedMs = 0)
            }

            copy(
                navigation = TvNavigation.openPlayer(
                    navigation,
                    episodeId,
                    animeId,
                ),
                episode = bundle,
                storageDecision = decision?.takeUnless {
                    it.primaryAction == TvStorageAction.PLAY
                },
                error = null,
            )
        }

    suspend fun refreshEpisodeStorage(
        elapsedMs: Long,
    ): TvAppSnapshot =
        runBusy {
            val bundle = episode
                ?: error("No TV episode is loaded.")
            val media = bundle.bootstrap.media
                ?: error("This episode has no media.")
            val availability = flow.refreshMediaAvailability(
                mediaFileId = media.mediaFileId,
                fresh = true,
            )
            val decision = TvStorageRecoveryPolicy.decide(
                availability,
                elapsedMs,
            )
            copy(
                storageDecision = decision.takeUnless {
                    it.primaryAction == TvStorageAction.PLAY
                },
                error = null,
            )
        }

    suspend fun wakeEpisodeStorage(): TvAppSnapshot =
        runBusy {
            val media = episode?.bootstrap?.media
                ?: error("No TV media is loaded.")
            val rootId = media.availability.rootId
                ?: error("Wake-on-LAN is not available for this media.")
            if (!media.availability.canWake) {
                error("Wake-on-LAN is not available for this profile.")
            }

            flow.wakeRoot(rootId)
            copy(
                storageDecision = TvStorageRecoveryPolicy.wakeDecision(
                    media.availability,
                ),
                error = null,
            )
        }

    suspend fun refreshCueWindow(
        positionMs: Long,
    ): TvAppSnapshot =
        runBusy {
            val bundle = episode
                ?: error("No TV episode is loaded.")
            val trackId = bundle.bootstrap.activeLearningSubtitleTrackId
                ?: return@runBusy copy(error = null)
            val refreshed = flow.loadCueWindow(
                episodeId = bundle.bootstrap.episode.id,
                trackId = trackId,
                positionMs = positionMs,
            )
            copy(
                episode = bundle.copy(cues = refreshed),
                error = null,
            )
        }

    suspend fun saveProgress(
        write: TvProgressWrite,
    ) {
        val bundle = snapshot.episode ?: return
        flow.saveProgress(
            episodeId = bundle.bootstrap.episode.id,
            positionMs = write.positionMs,
            durationMs = write.durationMs,
            completed = write.completed,
        )
    }

    suspend fun setTermState(
        termId: String,
        state: String,
    ): TvAppSnapshot =
        runBusy {
            flow.setTermState(termId, state)
            val bundle = episode
            if (bundle == null) {
                copy(error = null)
            } else {
                copy(
                    episode = bundle.copy(
                        cues = updateCueTermState(
                            bundle.cues,
                            termId,
                            state,
                        ),
                    ),
                    error = null,
                )
            }
        }

    suspend fun signOut(): TvAppSnapshot =
        runBusy {
            flow.logout()
            copy(
                navigation = TvNavigation.signOut(navigation),
                account = null,
                library = null,
                anime = null,
                episode = null,
                storageDecision = null,
                error = null,
            )
        }

    fun changeServer(): TvAppSnapshot {
        settings.clear()
        snapshot = TvAppSnapshot(
            navigation = TvNavigation.changeServer(),
        )
        return snapshot
    }

    fun back(): TvAppSnapshot? {
        val nextNavigation = TvNavigation.back(snapshot.navigation)
            ?: return null

        snapshot = snapshot.copy(
            navigation = nextNavigation,
            anime = when (nextNavigation.route) {
                TvRoute.Library,
                TvRoute.Login,
                TvRoute.Setup,
                -> null
                else -> snapshot.anime
            },
            episode = if (nextNavigation.route is TvRoute.Player) {
                snapshot.episode
            } else {
                null
            },
            storageDecision = null,
            error = null,
        )
        return snapshot
    }

    fun reportError(throwable: Throwable): TvAppSnapshot {
        snapshot = snapshot.copy(
            busy = false,
            error = throwable.message ?: "AniLingo TV request failed.",
        )
        return snapshot
    }

    private suspend fun runBusy(
        action: suspend TvAppSnapshot.() -> TvAppSnapshot,
    ): TvAppSnapshot {
        snapshot = snapshot.copy(busy = true, error = null)
        return try {
            snapshot.action().also {
                snapshot = it.copy(busy = false)
            }
        } catch (throwable: Throwable) {
            reportError(throwable)
        }
    }

    private fun updateCueTermState(
        cues: CueResponse,
        termId: String,
        state: String,
    ): CueResponse =
        cues.copy(
            cues = cues.cues.map { cue ->
                cue.copy(
                    tokens = cue.tokens.map { token ->
                        if (token.termId == termId) {
                            token.copy(state = state)
                        } else {
                            token
                        }
                    },
                )
            },
        )

    private suspend fun ensureConnected() {
        if (flow.origin == null) {
            val origin = settings.origin
                ?: error("Configure an AniLingo server first.")
            val capabilities = flow.connect(origin)
            snapshot = snapshot.copy(capabilities = capabilities)
        }
    }
}
