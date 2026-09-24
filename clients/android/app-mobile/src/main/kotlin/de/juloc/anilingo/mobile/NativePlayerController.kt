package de.juloc.anilingo.mobile

import android.content.Context
import android.media.MediaCodecList
import android.net.Uri
import android.os.SystemClock
import androidx.media3.common.C
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import de.juloc.anilingo.core.api.ClientApiHttpException
import de.juloc.anilingo.core.api.ClientApiRoutes
import de.juloc.anilingo.core.api.HttpAniLingoClientApi
import de.juloc.anilingo.core.model.ClientCapabilities
import de.juloc.anilingo.core.model.EpisodeProgressUpdate
import de.juloc.anilingo.core.model.MediaAvailability
import de.juloc.anilingo.core.model.MediaTrack
import de.juloc.anilingo.core.model.PlayerBootstrap
import de.juloc.anilingo.core.model.PlayerMedia
import de.juloc.anilingo.core.model.SubtitleCue
import de.juloc.anilingo.core.model.TermDetail
import de.juloc.anilingo.core.player.AniLingoMedia3Player
import de.juloc.anilingo.core.player.DevicePlaybackSupport
import de.juloc.anilingo.core.player.PlaybackSelector
import de.juloc.anilingo.core.player.PlaybackTransport
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import java.io.Closeable

data class NativePlayerUiState(
    val loading: Boolean = true,
    val updateRequired: String? = null,
    val error: String? = null,
    val bootstrap: PlayerBootstrap? = null,
    val transport: PlaybackTransport? = null,
    val positionMs: Long = 0,
    val durationMs: Long = 0,
    val isPlaying: Boolean = false,
    val currentCue: SubtitleCue? = null,
    val cues: List<SubtitleCue> = emptyList(),
    val selectedAudioTrackId: String? = null,
    val selectedSubtitleTrackId: String? = null,
    val selectedTerm: TermDetail? = null,
    val termLoading: Boolean = false,
    val storage: MediaAvailability? = null,
    val storageRetryExhausted: Boolean = false,
    val wakeInProgress: Boolean = false,
)

class NativePlayerController(
    context: Context,
    private val episodeId: String,
    private val origin: ServerOrigin,
    private val api: HttpAniLingoClientApi,
    private val sessionHeaders: () -> Map<String, String>,
) : Closeable {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val mediaPlayer = AniLingoMedia3Player(context.applicationContext)
    private val _state = MutableStateFlow(NativePlayerUiState())
    val state: StateFlow<NativePlayerUiState> = _state.asStateFlow()
    val player: Player
        get() = mediaPlayer.player

    private var capabilities: ClientCapabilities? = null
    private var bootstrap: PlayerBootstrap? = null
    private var sourceOffsetMs = 0L
    private var currentTransport: PlaybackTransport? = null
    private var recoveryPlayback: PreservedPlayback? = null
    private var recoveryJob: Job? = null
    private var lastProgressSentAt = 0L
    private var lastProgressPositionMs = -1L
    private var started = false

    private val playerListener = object : Player.Listener {
        override fun onIsPlayingChanged(isPlaying: Boolean) {
            _state.update { it.copy(isPlaying = isPlaying) }
        }

        override fun onPlaybackStateChanged(playbackState: Int) {
            if (playbackState == Player.STATE_ENDED) {
                scope.launch {
                    persistProgress(completed = true, force = true)
                }
            }
        }

        override fun onPlayerError(error: PlaybackException) {
            val preserved = preservePlayback()
            scope.launch {
                handlePlaybackFailure(error, preserved)
            }
        }
    }

    init {
        player.addListener(playerListener)
        scope.launch {
            while (isActive) {
                updatePlaybackClock()
                maybePersistProgress()
                delay(250)
            }
        }
    }

    fun start() {
        if (started) {
            return
        }
        started = true
        scope.launch {
            initialize()
        }
    }

    fun togglePlayPause() {
        if (player.isPlaying || player.playWhenReady) {
            player.pause()
            scope.launch { persistProgress(completed = false, force = true) }
        } else {
            player.play()
        }
    }

    fun seekBy(deltaMs: Long) {
        seekTo((absolutePositionMs() + deltaMs).coerceAtLeast(0))
    }

    fun seekTo(positionMs: Long) {
        val duration = _state.value.durationMs
        val target = if (duration > 0) {
            positionMs.coerceIn(0, duration)
        } else {
            positionMs.coerceAtLeast(0)
        }

        val shouldPlay = player.playWhenReady
        when (currentTransport) {
            PlaybackTransport.LIVE_MP4_FALLBACK -> {
                scope.launch {
                    openTransport(
                        transport = PlaybackTransport.LIVE_MP4_FALLBACK,
                        absolutePositionMs = target,
                        shouldPlay = shouldPlay,
                    )
                    persistProgress(completed = false, force = true)
                }
            }
            else -> {
                player.seekTo(target)
                _state.update { it.copy(positionMs = target) }
                scope.launch { persistProgress(completed = false, force = true) }
            }
        }
    }

    fun repeatCurrentCue() {
        val cue = _state.value.currentCue ?: return
        seekTo(cue.startMs.toLong())
        player.play()
    }

    fun selectAudioTrack(track: MediaTrack) {
        val builder = player.trackSelectionParameters.buildUpon()
        track.language?.takeIf { it.isNotBlank() }?.let {
            builder.setPreferredAudioLanguage(it)
        }
        player.trackSelectionParameters = builder.build()
        _state.update { it.copy(selectedAudioTrackId = track.id) }
    }

    fun selectSubtitleTrack(track: MediaTrack?) {
        val builder = player.trackSelectionParameters.buildUpon()
        if (track == null) {
            builder.setTrackTypeDisabled(C.TRACK_TYPE_TEXT, true)
        } else {
            builder.setTrackTypeDisabled(C.TRACK_TYPE_TEXT, false)
            track.language?.takeIf { it.isNotBlank() }?.let {
                builder.setPreferredTextLanguage(it)
            }
        }
        player.trackSelectionParameters = builder.build()
        _state.update { it.copy(selectedSubtitleTrackId = track?.id) }
    }

    fun loadTerm(termId: String) {
        scope.launch {
            _state.update { it.copy(termLoading = true) }
            try {
                val term = io { api.getTerm(termId) }
                _state.update {
                    it.copy(
                        selectedTerm = term,
                        termLoading = false,
                    )
                }
            } catch (exception: Exception) {
                _state.update {
                    it.copy(
                        error = userMessage(exception),
                        termLoading = false,
                    )
                }
            }
        }
    }

    fun clearSelectedTerm() {
        _state.update { it.copy(selectedTerm = null, termLoading = false) }
    }

    fun setSelectedTermState(stateName: String) {
        val term = _state.value.selectedTerm ?: return
        scope.launch {
            try {
                val result = io { api.setTermState(term.id, stateName) }
                _state.update {
                    it.copy(
                        selectedTerm = term.copy(state = result.state),
                    )
                }
            } catch (exception: Exception) {
                _state.update { it.copy(error = userMessage(exception)) }
            }
        }
    }

    fun retryStorage() {
        val preserved = recoveryPlayback ?: preservePlayback()
        scope.launch {
            retryStorageOnce(preserved, allowAutomaticContinuation = true)
        }
    }

    fun wakeStorage() {
        val availability = _state.value.storage ?: return
        if (!StorageRecoveryPolicy.canOfferWake(availability)) {
            return
        }

        val rootId = availability.rootId ?: return
        scope.launch {
            _state.update { it.copy(wakeInProgress = true, error = null) }
            try {
                io { api.wakeRoot(rootId) }
                _state.update { it.copy(wakeInProgress = false) }
                startStorageRecovery(
                    availability = availability.copy(
                        state = "source_starting",
                        retryable = true,
                    ),
                    preserved = recoveryPlayback ?: preservePlayback(),
                )
            } catch (exception: Exception) {
                _state.update {
                    it.copy(
                        wakeInProgress = false,
                        error = userMessage(exception),
                    )
                }
            }
        }
    }

    fun onBackgrounded() {
        scope.launch {
            persistProgress(completed = false, force = true)
        }
    }

    suspend fun persistBeforeClose() {
        persistProgress(completed = false, force = true)
    }

    private suspend fun initialize() {
        _state.update {
            it.copy(
                loading = true,
                error = null,
                updateRequired = null,
            )
        }

        try {
            val currentCapabilities = io { api.getCapabilities() }
            capabilities = currentCapabilities

            when (val compatibility = MobileCompatibilityGate.evaluate(currentCapabilities)) {
                MobileCompatibilityState.Compatible -> Unit
                is MobileCompatibilityState.UpdateRequired -> {
                    _state.update {
                        it.copy(
                            loading = false,
                            updateRequired = compatibility.detail,
                        )
                    }
                    return
                }
            }

            val progress = io { api.getProgress(episodeId) }
            val playerBootstrap = io { api.getPlayer(episodeId) }
            bootstrap = playerBootstrap

            val cues = loadCues(playerBootstrap)
            val resumePosition = if (!progress.isCompleted && progress.positionMs >= 5_000) {
                progress.positionMs
            } else {
                0L
            }

            _state.update {
                it.copy(
                    bootstrap = playerBootstrap,
                    cues = cues,
                    selectedAudioTrackId = playerBootstrap.defaultAudioTrackId,
                    selectedSubtitleTrackId = playerBootstrap.defaultSubtitleTrackId,
                    durationMs = playerBootstrap.media?.durationMs
                        ?: progress.durationMs
                        ?: 0L,
                )
            }

            preparePlayback(
                playerBootstrap = playerBootstrap,
                absolutePositionMs = resumePosition,
                shouldPlay = true,
            )
        } catch (exception: CancellationException) {
            throw exception
        } catch (exception: Exception) {
            _state.update {
                it.copy(
                    loading = false,
                    error = userMessage(exception),
                )
            }
        }
    }

    private suspend fun preparePlayback(
        playerBootstrap: PlayerBootstrap,
        absolutePositionMs: Long,
        shouldPlay: Boolean,
    ) {
        val media = playerBootstrap.media
        if (media == null) {
            _state.update {
                it.copy(
                    loading = false,
                    error = "This episode does not have playable media.",
                )
            }
            return
        }

        if (!media.availability.isAvailable) {
            startStorageRecovery(
                availability = media.availability,
                preserved = PreservedPlayback(
                    positionMs = absolutePositionMs,
                    shouldPlay = shouldPlay,
                ),
            )
            return
        }

        val currentCapabilities = capabilities
            ?: throw IllegalStateException("Client capabilities were not loaded.")

        val transport = PlaybackSelector.select(
            capabilities = currentCapabilities,
            bootstrap = playerBootstrap,
            device = DevicePlaybackSupport(
                directContainerAndCodecSupported = deviceSupports(media),
            ),
        )

        openTransport(
            transport = transport,
            absolutePositionMs = absolutePositionMs,
            shouldPlay = shouldPlay,
        )
    }

    private suspend fun openTransport(
        transport: PlaybackTransport,
        absolutePositionMs: Long,
        shouldPlay: Boolean,
    ) {
        val currentBootstrap = bootstrap
            ?: throw IllegalStateException("Player bootstrap was not loaded.")
        val media = currentBootstrap.media
            ?: throw IllegalStateException("Player media was not loaded.")

        val route = when (transport) {
            PlaybackTransport.DIRECT -> media.directContentUrl
            PlaybackTransport.HLS_FALLBACK ->
                currentBootstrap.fallback.url
                    ?: throw IllegalStateException("HLS fallback is not available.")
            PlaybackTransport.LIVE_MP4_FALLBACK ->
                ClientApiRoutes.fallback(
                    episodeId = episodeId,
                    startSeconds = absolutePositionMs / 1000.0,
                )
        }

        val resolved = origin.resolveSameOrigin(route)
        sourceOffsetMs = if (transport == PlaybackTransport.LIVE_MP4_FALLBACK) {
            absolutePositionMs
        } else {
            0L
        }
        currentTransport = transport

        mediaPlayer.open(
            uri = Uri.parse(resolved.toString()),
            startPositionMs = if (transport == PlaybackTransport.LIVE_MP4_FALLBACK) {
                0L
            } else {
                absolutePositionMs
            },
            playWhenReady = shouldPlay,
            requestHeaders = sessionHeaders(),
        )

        recoveryJob?.cancel()
        recoveryJob = null
        recoveryPlayback = null
        _state.update {
            it.copy(
                loading = false,
                error = null,
                storage = null,
                storageRetryExhausted = false,
                transport = transport,
                positionMs = absolutePositionMs,
                durationMs = media.durationMs ?: it.durationMs,
            )
        }
    }

    private suspend fun handlePlaybackFailure(
        error: PlaybackException,
        preserved: PreservedPlayback,
    ) {
        val currentBootstrap = bootstrap
        if (currentTransport == PlaybackTransport.DIRECT &&
            PlaybackFailureClassifier.shouldUseServerFallback(error.errorCode) &&
            currentBootstrap?.fallback?.available == true &&
            !currentBootstrap.fallback.url.isNullOrBlank()
        ) {
            val fallbackTransport = if (
                currentBootstrap.fallback.kind == "hls" &&
                capabilities?.features?.hlsFallback == true
            ) {
                PlaybackTransport.HLS_FALLBACK
            } else {
                PlaybackTransport.LIVE_MP4_FALLBACK
            }

            openTransport(
                transport = fallbackTransport,
                absolutePositionMs = preserved.positionMs,
                shouldPlay = preserved.shouldPlay,
            )
            return
        }

        if (PlaybackFailureClassifier.isNetworkFailure(error.errorCode)) {
            val mediaId = currentBootstrap?.media?.mediaFileId
            if (mediaId != null) {
                val availability = runCatching {
                    io { api.getMediaAvailability(mediaId, fresh = true) }
                }.getOrNull()

                if (availability != null && !availability.isAvailable) {
                    startStorageRecovery(
                        availability = availability,
                        preserved = preserved,
                    )
                    return
                }
            }
        }

        _state.update {
            it.copy(
                loading = false,
                error = "Playback failed: ${error.message ?: "unknown Media3 error"}",
            )
        }
    }

    private fun startStorageRecovery(
        availability: MediaAvailability,
        preserved: PreservedPlayback,
    ) {
        recoveryJob?.cancel()
        recoveryPlayback = preserved
        player.stop()

        _state.update {
            it.copy(
                loading = false,
                error = null,
                storage = availability,
                storageRetryExhausted = false,
                positionMs = preserved.positionMs,
                isPlaying = false,
            )
        }

        if (!StorageRecoveryPolicy.shouldAutomaticallyRetry(availability)) {
            return
        }

        recoveryJob = scope.launch {
            val startedAt = SystemClock.elapsedRealtime()
            var attempt = 0

            while (isActive &&
                SystemClock.elapsedRealtime() - startedAt < StorageRecoveryPolicy.MaxAutomaticRetryMs
            ) {
                delay(StorageRecoveryPolicy.delayForAttempt(attempt))
                attempt += 1

                val recovered = retryStorageOnce(
                    preserved = preserved,
                    allowAutomaticContinuation = false,
                )
                if (recovered) {
                    return@launch
                }

                val currentAvailability = _state.value.storage
                if (currentAvailability != null &&
                    !StorageRecoveryPolicy.shouldAutomaticallyRetry(currentAvailability)
                ) {
                    return@launch
                }
            }

            _state.update { it.copy(storageRetryExhausted = true) }
        }
    }

    private suspend fun retryStorageOnce(
        preserved: PreservedPlayback,
        allowAutomaticContinuation: Boolean,
    ): Boolean {
        val mediaId = bootstrap?.media?.mediaFileId ?: return false

        return try {
            val availability = io {
                api.getMediaAvailability(
                    mediaFileId = mediaId,
                    fresh = true,
                )
            }

            _state.update {
                it.copy(
                    storage = availability,
                    storageRetryExhausted = false,
                    error = null,
                )
            }

            if (availability.isAvailable) {
                val refreshed = io { api.getPlayer(episodeId) }
                bootstrap = refreshed
                val cues = loadCues(refreshed)
                _state.update {
                    it.copy(
                        bootstrap = refreshed,
                        cues = cues,
                        durationMs = refreshed.media?.durationMs ?: it.durationMs,
                    )
                }
                preparePlayback(
                    playerBootstrap = refreshed,
                    absolutePositionMs = preserved.positionMs,
                    shouldPlay = preserved.shouldPlay,
                )
                true
            } else {
                if (allowAutomaticContinuation &&
                    StorageRecoveryPolicy.shouldAutomaticallyRetry(availability)
                ) {
                    startStorageRecovery(
                        availability = availability,
                        preserved = preserved,
                    )
                } else if (!StorageRecoveryPolicy.shouldAutomaticallyRetry(availability)) {
                    _state.update { it.copy(storageRetryExhausted = true) }
                }
                false
            }
        } catch (exception: Exception) {
            _state.update {
                it.copy(
                    error = userMessage(exception),
                    storageRetryExhausted = true,
                )
            }
            false
        }
    }

    private suspend fun loadCues(playerBootstrap: PlayerBootstrap): List<SubtitleCue> {
        val trackId = playerBootstrap.activeLearningSubtitleTrackId ?: return emptyList()
        return io {
            api.getCues(
                episodeId = episodeId,
                trackId = trackId,
            ).cues
        }
    }

    private fun updatePlaybackClock() {
        val absolute = absolutePositionMs()
        val cues = _state.value.cues
        val cue = cues.firstOrNull { absolute in it.startMs.toLong() until it.endMs.toLong() }

        _state.update {
            it.copy(
                positionMs = absolute,
                isPlaying = player.isPlaying,
                currentCue = cue,
            )
        }
    }

    private suspend fun maybePersistProgress() {
        if (!player.isPlaying) {
            return
        }

        val now = SystemClock.elapsedRealtime()
        if (now - lastProgressSentAt < 15_000) {
            return
        }

        persistProgress(completed = false, force = false)
    }

    private suspend fun persistProgress(
        completed: Boolean,
        force: Boolean,
    ) {
        val currentCapabilities = capabilities ?: return
        if (!currentCapabilities.features.playbackProgress) {
            return
        }

        val position = absolutePositionMs()
        if (!force && position == lastProgressPositionMs) {
            return
        }

        val now = SystemClock.elapsedRealtime()
        if (!force && now - lastProgressSentAt < 15_000) {
            return
        }

        lastProgressSentAt = now
        lastProgressPositionMs = position

        runCatching {
            io {
                api.setProgress(
                    episodeId = episodeId,
                    update = EpisodeProgressUpdate(
                        positionMs = position,
                        durationMs = _state.value.durationMs.takeIf { it > 0 },
                        completed = completed,
                    ),
                )
            }
        }
    }

    private fun preservePlayback(): PreservedPlayback =
        PlaybackPositionPolicy.preserve(
            playerPositionMs = player.currentPosition.coerceAtLeast(0),
            sourceOffsetMs = sourceOffsetMs,
            shouldPlay = player.playWhenReady && player.playbackState != Player.STATE_ENDED,
        )

    private fun absolutePositionMs(): Long =
        PlaybackPositionPolicy.absolutePosition(
            playerPositionMs = player.currentPosition.coerceAtLeast(0),
            sourceOffsetMs = sourceOffsetMs,
        )

    private fun deviceSupports(media: PlayerMedia): Boolean {
        val mimeType = when (media.videoCodec?.lowercase()) {
            "h264", "avc", "avc1" -> "video/avc"
            "h265", "hevc", "hev1", "hvc1" -> "video/hevc"
            "vp8" -> "video/x-vnd.on2.vp8"
            "vp9" -> "video/x-vnd.on2.vp9"
            "av1", "av01" -> "video/av01"
            "mpeg4", "mp4v" -> "video/mp4v-es"
            null, "" -> return true
            else -> return true
        }

        return runCatching {
            MediaCodecList(MediaCodecList.REGULAR_CODECS)
                .codecInfos
                .asSequence()
                .filter { !it.isEncoder }
                .flatMap { it.supportedTypes.asSequence() }
                .any { it.equals(mimeType, ignoreCase = true) }
        }.getOrDefault(true)
    }

    private suspend fun <T> io(block: suspend () -> T): T =
        withContext(Dispatchers.IO) { block() }

    private fun userMessage(exception: Exception): String =
        when (exception) {
            is ClientApiHttpException -> when (exception.statusCode) {
                401 -> "Sign in to AniLingo in the app before starting native playback."
                403 -> "Your AniLingo account is not allowed to perform this action."
                else -> exception.message
            }
            else -> exception.message ?: "AniLingo could not complete the request."
        }

    override fun close() {
        recoveryJob?.cancel()
        player.removeListener(playerListener)
        mediaPlayer.close()
        scope.cancel()
    }
}
