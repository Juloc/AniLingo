package de.juloc.anilingo.tv

import android.net.Uri
import android.os.SystemClock
import androidx.activity.compose.BackHandler
import androidx.annotation.OptIn
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Button
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Surface
import androidx.tv.material3.Text
import de.juloc.anilingo.core.player.AniLingoMedia3Player
import de.juloc.anilingo.core.player.PlaybackTransport
import de.juloc.anilingo.core.session.PlaybackCommand
import de.juloc.anilingo.core.session.PlaybackPairing
import de.juloc.anilingo.core.session.PlaybackSessionToken
import de.juloc.anilingo.core.session.PlaybackSessionUpdate
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import androidx.media3.common.util.UnstableApi
import java.net.URI

@OptIn(UnstableApi::class)
@Composable
fun TvAppHost(
    controller: TvAppController,
    settings: TvServerSettings,
    cookies: TvSessionCookieStore,
    player: AniLingoMedia3Player,
    onFinish: () -> Unit,
) {
    val scope = rememberCoroutineScope()
    var snapshot by remember { mutableStateOf(controller.snapshot) }
    var openedEpisodeId by remember { mutableStateOf<String?>(null) }
    var playbackGeneration by remember { mutableIntStateOf(0) }
    var forceFallback by remember { mutableStateOf(false) }
    var currentTransport by remember { mutableStateOf<PlaybackTransport?>(null) }
    var resumePositionMs by remember { mutableLongStateOf(0L) }
    var resumeShouldPlay by remember { mutableStateOf(true) }
    var currentPositionMs by remember { mutableLongStateOf(0L) }
    var currentDurationMs by remember { mutableLongStateOf(0L) }
    var selectedAudioTrackId by remember { mutableStateOf<String?>(null) }
    var selectedSubtitleTrackId by remember { mutableStateOf<String?>(null) }
    var cueRefreshRunning by remember { mutableStateOf(false) }
    var companionRuntime by remember { mutableStateOf<TvCompanionRuntime?>(null) }
    var companionPairing by remember { mutableStateOf<PlaybackPairing?>(null) }
    var companionBusy by remember { mutableStateOf(false) }
    var remoteCommand by remember { mutableStateOf<PlaybackCommand?>(null) }
    var companionSelectedTermId by remember { mutableStateOf<String?>(null) }
    var lastCompanionPushAt by remember { mutableLongStateOf(0L) }
    var lastCompanionCueId by remember { mutableStateOf<Long?>(null) }
    var lastCompanionPlaying by remember { mutableStateOf<Boolean?>(null) }

    val playerRoute = snapshot.navigation.route as? TvRoute.Player
    val episodeBundle = snapshot.episode
    val episodeId = playerRoute?.episodeId
    val progressPolicy = remember(episodeId) { TvProgressPolicy() }

    fun launchSnapshot(block: suspend () -> TvAppSnapshot) {
        snapshot = snapshot.copy(busy = true, error = null)
        scope.launch {
            snapshot = block()
        }
    }

    fun currentCompanionUpdate(): PlaybackSessionUpdate? {
        val bundle = snapshot.episode ?: return null
        val cue = TvCueTimeline.currentCue(
            bundle.cues,
            currentPositionMs,
        )
        val selectedTermId = companionSelectedTermId?.takeIf { selected ->
            cue?.tokens?.any { it.termId == selected } == true
        }

        return PlaybackSessionUpdate(
            animeTitle = bundle.bootstrap.episode.animeTitle,
            episodeTitle = bundle.bootstrap.episode.title,
            positionMs = currentPositionMs.coerceAtLeast(0),
            durationMs = currentDurationMs.takeIf { it > 0 },
            isPlaying = player.player.isPlaying,
            playbackRate = player.player.playbackParameters.speed.toDouble(),
            audioTrackId = selectedAudioTrackId,
            subtitleTrackId = selectedSubtitleTrackId,
            currentCueId = cue?.id,
            currentCueText = cue?.text,
            currentCueTokens = cue?.tokens?.map { token ->
                PlaybackSessionToken(
                    surface = token.surface,
                    termId = token.termId,
                    canonical = token.canonical,
                    reading = token.reading,
                    meaning = token.meaning,
                    state = token.state,
                )
            }.orEmpty(),
            selectedTermId = selectedTermId,
        )
    }

    fun endCompanionSession() {
        val runtime = companionRuntime ?: return
        companionRuntime = null
        companionPairing = null
        companionSelectedTermId = null
        remoteCommand = null
        lastCompanionPushAt = 0
        lastCompanionCueId = null
        lastCompanionPlaying = null

        scope.launch(Dispatchers.IO) {
            runCatching { runtime.end() }
        }
    }

    fun pushCompanionState(force: Boolean = false) {
        val runtime = companionRuntime ?: return
        val update = currentCompanionUpdate() ?: return
        val cueId = update.currentCueId
        val playing = update.isPlaying
        val now = SystemClock.elapsedRealtime()
        val importantChange =
            cueId != lastCompanionCueId ||
                playing != lastCompanionPlaying

        if (!force &&
            !importantChange &&
            now - lastCompanionPushAt < 1_000
        ) {
            return
        }

        lastCompanionPushAt = now
        lastCompanionCueId = cueId
        lastCompanionPlaying = playing

        scope.launch {
            runCatching {
                withContext(Dispatchers.IO) {
                    runtime.update(update)
                }
            }
        }
    }

    fun resetPlaybackRuntime() {
        endCompanionSession()
        player.player.stop()
        openedEpisodeId = null
        currentTransport = null
        forceFallback = false
        resumePositionMs = 0
        resumeShouldPlay = true
        currentPositionMs = 0
        currentDurationMs = 0
        selectedAudioTrackId = null
        selectedSubtitleTrackId = null
        cueRefreshRunning = false
    }

    fun persist(
        event: TvProgressEvent,
        positionMs: Long,
        durationMs: Long,
    ) {
        val write = progressPolicy.evaluate(
            event = event,
            nowMs = SystemClock.elapsedRealtime(),
            positionMs = positionMs,
            durationMs = durationMs.takeIf { it > 0 },
        ) ?: return

        scope.launch {
            controller.saveProgress(write)
        }
    }

    fun refreshCueWindowIfNeeded(positionMs: Long) {
        val bundle = snapshot.episode ?: return
        if (cueRefreshRunning ||
            bundle.bootstrap.activeLearningSubtitleTrackId == null ||
            !TvCueTimeline.shouldRefresh(bundle.cues, positionMs)
        ) {
            return
        }

        cueRefreshRunning = true
        scope.launch {
            snapshot = controller.refreshCueWindow(positionMs)
            cueRefreshRunning = false
            pushCompanionState(force = true)
        }
    }

    LaunchedEffect(Unit) {
        if (settings.origin != null &&
            snapshot.capabilities == null &&
            snapshot.navigation.route == TvRoute.Login
        ) {
            snapshot = controller.restoreConnection()
        }
    }

    LaunchedEffect(episodeId) {
        if (episodeId == null) {
            resetPlaybackRuntime()
            return@LaunchedEffect
        }

        val bundle = snapshot.episode ?: return@LaunchedEffect
        if (bundle.bootstrap.episode.id == episodeId) {
            resumePositionMs = bundle.progress.positionMs
            currentPositionMs = bundle.progress.positionMs
            currentDurationMs = bundle.progress.durationMs ?: 0
            selectedAudioTrackId = bundle.bootstrap.defaultAudioTrackId
            selectedSubtitleTrackId = bundle.bootstrap.defaultSubtitleTrackId
            forceFallback = false
            openedEpisodeId = null
            currentTransport = null
        }

        val supportsCompanion =
            snapshot.capabilities?.features?.playbackSessions == true &&
                snapshot.capabilities?.features?.companionPairing == true &&
                snapshot.capabilities?.features?.companionControl == true
        val origin = settings.origin
        val initial = currentCompanionUpdate()

        if (supportsCompanion &&
            origin != null &&
            initial != null
        ) {
            endCompanionSession()
            val runtime = TvCompanionRuntime(
                serverOrigin = origin,
                requestHeaders = cookies::requestHeaders,
            )

            val started = runCatching {
                withContext(Dispatchers.IO) {
                    runtime.start(
                        episodeId = episodeId,
                        initial = initial,
                        onState = { },
                        onCommand = { command ->
                            scope.launch {
                                remoteCommand = command
                            }
                        },
                        onEnded = {
                            scope.launch {
                                companionRuntime = null
                                companionPairing = null
                                remoteCommand = null
                            }
                        },
                    )
                }
            }

            if (started.isSuccess) {
                companionRuntime = runtime
                lastCompanionPushAt = SystemClock.elapsedRealtime()
                lastCompanionCueId = started.getOrNull()?.currentCueId
                lastCompanionPlaying = started.getOrNull()?.isPlaying
            } else {
                runCatching {
                    withContext(Dispatchers.IO) {
                        runtime.close()
                    }
                }
            }
        }
    }

    LaunchedEffect(
        episodeId,
        snapshot.storageDecision,
        playbackGeneration,
    ) {
        val route = playerRoute ?: return@LaunchedEffect
        val bundle = snapshot.episode ?: return@LaunchedEffect
        val capabilities = snapshot.capabilities ?: return@LaunchedEffect
        val origin = settings.origin ?: return@LaunchedEffect

        if (snapshot.storageDecision != null ||
            openedEpisodeId == route.episodeId
        ) {
            return@LaunchedEffect
        }

        val directSupported = !forceFallback &&
            TvDeviceCodecSupport.supportsDirectPlayback(bundle.bootstrap)

        val plan = runCatching {
            TvPlaybackPlanner.plan(
                serverOrigin = origin,
                capabilities = capabilities,
                bootstrap = bundle.bootstrap,
                directSupported = directSupported,
                startPositionMs = resumePositionMs,
            )
        }.getOrElse {
            snapshot = controller.reportError(it)
            return@LaunchedEffect
        }

        currentTransport = plan.transport
        player.open(
            uri = Uri.parse(plan.uri),
            startPositionMs = plan.startPositionMs,
            playWhenReady = resumeShouldPlay,
            requestHeaders = cookies.requestHeaders(),
        )
        openedEpisodeId = route.episodeId
    }

    LaunchedEffect(
        episodeId,
        snapshot.storageDecision?.state,
        snapshot.storageDecision?.retryAfterMs,
    ) {
        var decision = snapshot.storageDecision ?: return@LaunchedEffect
        if (decision.primaryAction != TvStorageAction.RETRY) {
            return@LaunchedEffect
        }

        val startedAt = SystemClock.elapsedRealtime()
        while (decision.primaryAction == TvStorageAction.RETRY) {
            delay(decision.retryAfterMs.toLong().coerceAtLeast(250L))
            val elapsed = SystemClock.elapsedRealtime() - startedAt
            val updated = controller.refreshEpisodeStorage(elapsed)
            snapshot = updated

            val next = updated.storageDecision
            if (next == null) {
                openedEpisodeId = null
                playbackGeneration += 1
                break
            }

            decision = next
        }
    }

    if (playerRoute == null) {
        BackHandler {
            val next = controller.back()
            if (next == null) {
                onFinish()
            } else {
                snapshot = next
            }
        }
    }

    when (val route = snapshot.navigation.route) {
        TvRoute.Setup -> TvSetupScreen(
            initialOrigin = settings.origin.orEmpty(),
            error = snapshot.error,
            busy = snapshot.busy,
            onConnect = { origin ->
                launchSnapshot {
                    cookies.clear()
                    controller.connect(origin)
                }
            },
        )

        TvRoute.Login -> TvLoginScreen(
            serverOrigin = settings.origin.orEmpty(),
            error = snapshot.error,
            busy = snapshot.busy,
            onLogin = { userName, password ->
                launchSnapshot {
                    controller.login(userName, password)
                }
            },
            onChangeServer = {
                cookies.clear()
                snapshot = controller.changeServer()
            },
        )

        TvRoute.Library -> {
            val account = snapshot.account
            val library = snapshot.library
            if (account == null || library == null) {
                TvMessageScreen(
                    title = "Library unavailable",
                    message = snapshot.error
                        ?: "Sign in again to load your AniLingo library.",
                    action = "Sign in",
                    onAction = {
                        cookies.clear()
                        snapshot = controller.changeServer()
                    },
                )
            } else {
                TvLibraryScreen(
                    account = account,
                    library = library,
                    error = snapshot.error,
                    onAnime = { anime ->
                        launchSnapshot { controller.openAnime(anime.id) }
                    },
                    onRefresh = {
                        launchSnapshot { controller.refreshLibrary() }
                    },
                    onSignOut = {
                        launchSnapshot {
                            val next = controller.signOut()
                            cookies.clear()
                            next
                        }
                    },
                )
            }
        }

        is TvRoute.Anime -> {
            val anime = snapshot.anime
            if (anime == null) {
                TvMessageScreen(
                    title = "Anime unavailable",
                    message = snapshot.error ?: "Could not load this anime.",
                    action = "Back",
                    onAction = {
                        controller.back()?.let { snapshot = it }
                    },
                )
            } else {
                TvAnimeScreen(
                    anime = anime,
                    onEpisode = { episode ->
                        launchSnapshot {
                            controller.openEpisode(
                                episodeId = episode.id,
                                animeId = anime.id,
                            )
                        }
                    },
                    onBack = {
                        controller.back()?.let { snapshot = it }
                    },
                )
            }
        }

        is TvRoute.Player -> {
            val bundle = episodeBundle
            if (bundle == null) {
                TvMessageScreen(
                    title = "Episode unavailable",
                    message = snapshot.error ?: "Could not load this episode.",
                    action = "Back",
                    onAction = {
                        controller.back()?.let { snapshot = it }
                    },
                )
            } else if (snapshot.storageDecision != null) {
                TvStorageRecoveryScreen(
                    decision = snapshot.storageDecision!!,
                    busy = snapshot.busy,
                    onRetry = {
                        scope.launch {
                            val updated = controller.refreshEpisodeStorage(0)
                            snapshot = updated
                            if (updated.storageDecision == null) {
                                openedEpisodeId = null
                                playbackGeneration += 1
                            }
                        }
                    },
                    onWake = {
                        scope.launch {
                            snapshot = controller.wakeEpisodeStorage()
                        }
                    },
                    onBack = {
                        resetPlaybackRuntime()
                        controller.back()?.let { snapshot = it }
                    },
                )
            } else {
                val cue = TvCueTimeline.currentCue(
                    bundle.cues,
                    currentPositionMs,
                )

                TvPlayerScreen(
                    player = player,
                    episodeTitle = bundle.bootstrap.episode.title,
                    currentCue = cue,
                    audioTracks = bundle.bootstrap.audioTracks,
                    subtitleTracks = bundle.bootstrap.subtitleTracks,
                    selectedAudioTrackId = selectedAudioTrackId,
                    selectedSubtitleTrackId = selectedSubtitleTrackId,
                    onSelectAudioTrack = { id ->
                        bundle.bootstrap.audioTracks
                            .firstOrNull { it.id == id }
                            ?.let { track ->
                                if (TvMediaTrackSelector.selectAudio(
                                        player.player,
                                        track,
                                    )
                                ) {
                                    selectedAudioTrackId = id
                                    pushCompanionState(force = true)
                                }
                            }
                    },
                    onSelectSubtitleTrack = { id ->
                        val track = id?.let { selected ->
                            bundle.bootstrap.subtitleTracks
                                .firstOrNull { it.id == selected }
                        }
                        if (TvMediaTrackSelector.selectSubtitle(
                                player.player,
                                track,
                            )
                        ) {
                            selectedSubtitleTrackId = id
                            pushCompanionState(force = true)
                        }
                    },
                    onPositionChanged = { position, duration, isPlaying ->
                        currentPositionMs = position
                        currentDurationMs = duration
                        resumePositionMs = position
                        resumeShouldPlay = isPlaying
                        persist(
                            event = if (isPlaying) {
                                TvProgressEvent.HEARTBEAT
                            } else {
                                TvProgressEvent.PAUSE
                            },
                            positionMs = position,
                            durationMs = duration,
                        )
                        refreshCueWindowIfNeeded(position)
                        pushCompanionState()
                    },
                    onSeeked = { position, duration, isPlaying ->
                        currentPositionMs = position
                        currentDurationMs = duration
                        resumePositionMs = position
                        resumeShouldPlay = isPlaying
                        persist(
                            TvProgressEvent.SEEK,
                            position,
                            duration,
                        )
                        refreshCueWindowIfNeeded(position)
                        pushCompanionState(force = true)
                    },
                    onPlaybackFailure = { position ->
                        resumePositionMs = position
                        resumeShouldPlay = true
                        player.player.stop()
                        openedEpisodeId = null

                        scope.launch {
                            val storage = controller.refreshEpisodeStorage(0)
                            snapshot = storage
                            if (storage.storageDecision != null) {
                                return@launch
                            }

                            if (currentTransport == PlaybackTransport.DIRECT) {
                                forceFallback = true
                                playbackGeneration += 1
                            } else {
                                snapshot = controller.reportError(
                                    IllegalStateException(
                                        "Server compatibility playback failed.",
                                    ),
                                )
                            }
                        }
                    },
                    canOpenOnPhone =
                        companionRuntime != null &&
                            snapshot.capabilities?.features?.companionControl == true &&
                            snapshot.capabilities?.features?.playbackSessions == true,
                    remoteCommand = remoteCommand,
                    companionVisible = companionPairing != null,
                    onCloseCompanion = {
                        companionPairing = null
                    },
                    companionOverlay = companionPairing?.let { pairing ->
                        {
                            val origin = settings.origin.orEmpty()
                            val base = URI(origin.trimEnd('/') + "/")
                            val companionUrl = base
                                .resolve(pairing.companionUrl.removePrefix("/"))
                                .toString()

                            TvCompanionPairingOverlay(
                                pairing = pairing,
                                companionUrl = companionUrl,
                                busy = companionBusy,
                                onNewCode = {
                                    val runtime = companionRuntime
                                    if (runtime != null) {
                                        scope.launch {
                                            companionBusy = true
                                            companionPairing = runCatching {
                                                withContext(Dispatchers.IO) {
                                                    runtime.createPairing()
                                                }
                                            }.getOrNull()
                                            companionBusy = false
                                        }
                                    }
                                },
                                onRevoke = {
                                    val runtime = companionRuntime
                                    if (runtime != null) {
                                        scope.launch {
                                            companionBusy = true
                                            runCatching {
                                                withContext(Dispatchers.IO) {
                                                    runtime.revoke()
                                                }
                                            }
                                            companionPairing = null
                                            companionBusy = false
                                        }
                                    }
                                },
                                onClose = {
                                    companionPairing = null
                                },
                            )
                        }
                    },
                    onSetTermState = { termId, state ->
                        scope.launch {
                            snapshot = controller.setTermState(termId, state)
                            pushCompanionState(force = true)
                        }
                    },
                    onSelectedTermChanged = { termId ->
                        companionSelectedTermId = termId
                        pushCompanionState(force = true)
                    },
                    onOpenOnPhone = { _, termId ->
                        companionSelectedTermId = termId
                        pushCompanionState(force = true)
                        val runtime = companionRuntime
                        if (runtime != null) {
                            scope.launch {
                                companionBusy = true
                                companionPairing = runCatching {
                                    withContext(Dispatchers.IO) {
                                        runtime.createPairing()
                                    }
                                }.getOrNull()
                                companionBusy = false
                            }
                        }
                    },
                    onExit = {
                        val write = progressPolicy.evaluate(
                            event = TvProgressEvent.CLOSE,
                            nowMs = SystemClock.elapsedRealtime(),
                            positionMs = currentPositionMs,
                            durationMs = currentDurationMs.takeIf { it > 0 },
                        )

                        scope.launch {
                            if (write != null) {
                                controller.saveProgress(write)
                            }
                            resetPlaybackRuntime()
                            controller.back()?.let { snapshot = it }
                        }
                    },
                )
            }
        }
    }
}

@Composable
private fun TvMessageScreen(
    title: String,
    message: String,
    action: String,
    onAction: () -> Unit,
) {
    Surface(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier.padding(56.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp),
        ) {
            Text(title, style = MaterialTheme.typography.headlineLarge)
            Text(message, style = MaterialTheme.typography.bodyLarge)
            Button(onClick = onAction) {
                Text(action)
            }
        }
    }
}
