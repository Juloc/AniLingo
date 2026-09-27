package de.juloc.jularr.tv

import de.juloc.jularr.core.api.JularrClientApi
import de.juloc.jularr.core.model.AnimeDetail
import de.juloc.jularr.core.model.ClientAccount
import de.juloc.jularr.core.model.ClientCapabilities
import de.juloc.jularr.core.model.ClientLibrary
import de.juloc.jularr.core.model.ContinueWatchingItem
import de.juloc.jularr.core.model.CueResponse
import de.juloc.jularr.core.model.PlaybackHistoryItem

data class TvAppSnapshot(
    val navigation: TvNavigationState,
    val capabilities: ClientCapabilities? = null,
    val account: ClientAccount? = null,
    val library: ClientLibrary? = null,
    val continueWatching: List<ContinueWatchingItem> = emptyList(),
    val activity: List<PlaybackHistoryItem> = emptyList(),
    /**
     * True when the server has no `/me/playback-history` data (older server, or the
     * feature flag is off): Activity then shows Continue Watching instead of a dead
     * screen, and the UI notes why (#522 item 4).
     */
    val activityUsesContinueWatchingFallback: Boolean = false,
    val anime: AnimeDetail? = null,
    val episodePage: TvEpisodePageData? = null,
    val episode: TvEpisodeBundle? = null,
    val storageDecision: TvStorageDecision? = null,
    val busy: Boolean = false,
    val error: String? = null,
)

class TvAppController(
    private val settings: TvServerOriginStore,
    apiFactory: (String) -> JularrClientApi,
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
            val resolvedCapabilities = ensureConnected()
            val signedIn = flow.login(userName, password, resolvedCapabilities)
            copy(
                navigation = TvNavigation.signedIn(navigation),
                account = signedIn.account,
                library = signedIn.library,
                continueWatching = signedIn.continueWatching,
                anime = null,
                episodePage = null,
                episode = null,
                activity = emptyList(),
                activityUsesContinueWatchingFallback = false,
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

    /**
     * Switches to one of the four sidebar destinations (#522). Home and Activity reload
     * their data on the way in (Home's library/continue-watching, Activity's playback
     * history or its Continue Watching fallback) rather than needing a manual refresh
     * control.
     */
    suspend fun selectSidebarRoute(route: TvRoute): TvAppSnapshot =
        runBusy {
            val withContent = when (route) {
                TvRoute.Home -> copy(
                    library = flow.refreshLibrary(),
                    continueWatching = if (capabilities?.features?.continueWatching == true) {
                        flow.loadContinueWatching()
                    } else {
                        emptyList()
                    },
                )

                TvRoute.Activity -> if (capabilities?.features?.playbackHistory == true) {
                    copy(
                        activity = flow.loadPlaybackHistory(),
                        activityUsesContinueWatchingFallback = false,
                    )
                } else {
                    copy(activityUsesContinueWatchingFallback = true)
                }

                else -> this
            }

            withContent.copy(
                navigation = TvNavigation.openSidebarRoute(navigation, route),
                anime = null,
                episodePage = null,
                episode = null,
                storageDecision = null,
                error = null,
            )
        }

    fun openSearch(): TvAppSnapshot {
        snapshot = snapshot.copy(
            navigation = TvNavigation.openSearch(snapshot.navigation),
            error = null,
        )
        return snapshot
    }

    suspend fun openAnime(animeId: String): TvAppSnapshot =
        runBusy {
            val loaded = flow.loadAnime(animeId)
            copy(
                navigation = TvNavigation.openAnime(navigation, animeId),
                anime = loaded,
                episodePage = null,
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
            copy(
                navigation = TvNavigation.openEpisode(
                    navigation,
                    episodeId,
                    animeId,
                ),
                episodePage = flow.loadEpisodePage(episodeId),
                episode = null,
                storageDecision = null,
                error = null,
            )
        }

    suspend fun playEpisode(
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
        val progress = flow.saveProgress(
            episodeId = bundle.bootstrap.episode.id,
            positionMs = write.positionMs,
            durationMs = write.durationMs,
            completed = write.completed,
        )
        val page = snapshot.episodePage
        if (page?.detail?.id == bundle.bootstrap.episode.id) {
            snapshot = snapshot.copy(
                episodePage = page.copy(progress = progress),
            )
        }
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
                continueWatching = emptyList(),
                activity = emptyList(),
                activityUsesContinueWatchingFallback = false,
                anime = null,
                episodePage = null,
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

        val stillBrowsingAnime = nextNavigation.route is TvRoute.Anime ||
            nextNavigation.route is TvRoute.Episode ||
            nextNavigation.route is TvRoute.Player

        snapshot = snapshot.copy(
            navigation = nextNavigation,
            anime = if (stillBrowsingAnime) snapshot.anime else null,
            episodePage = when (nextNavigation.route) {
                is TvRoute.Episode,
                is TvRoute.Player,
                -> snapshot.episodePage
                else -> null
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
            error = throwable.message ?: "Jularr TV request failed.",
        )
        return snapshot
    }

    private suspend fun runBusy(
        action: suspend TvAppSnapshot.() -> TvAppSnapshot,
    ): TvAppSnapshot {
        snapshot = snapshot.copy(busy = true, error = null)
        return try {
            // The action runs against the busy snapshot, so its result still
            // carries busy = true. Clear it on the value we store *and* return:
            // TvAppHost assigns the returned snapshot directly to the UI state.
            snapshot.action().copy(busy = false).also { snapshot = it }
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

    private suspend fun TvAppSnapshot.ensureConnected(): ClientCapabilities {
        capabilities?.let { return it }
        val origin = settings.origin
            ?: error("Configure an Jularr server first.")
        val resolved = flow.connect(origin)
        snapshot = snapshot.copy(capabilities = resolved)
        return resolved
    }
}
