package de.juloc.anilingo.mobile

import androidx.annotation.OptIn
import android.content.Context
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.runtime.collectAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.media3.common.util.UnstableApi
import androidx.media3.ui.AspectRatioFrameLayout
import androidx.media3.ui.PlayerView
import de.juloc.anilingo.core.api.HttpAniLingoClientApi
import de.juloc.anilingo.core.model.MediaAvailability
import de.juloc.anilingo.core.model.MediaTrack
import de.juloc.anilingo.core.model.SubtitleCue
import de.juloc.anilingo.core.player.PlaybackTransport
import kotlinx.coroutines.launch

@OptIn(UnstableApi::class)
@Composable
fun NativePlayerScreen(
    episodeId: String,
    origin: ServerOrigin,
    api: HttpAniLingoClientApi,
    sessionHeaders: () -> Map<String, String>,
    onClose: () -> Unit,
) {
    val context = LocalContext.current
    val activity = context as? ComponentActivity
    val scope = rememberCoroutineScope()
    val design = remember { MobilePlayerDesignLoader.load(context) }
    val controller = remember(episodeId, origin.value) {
        NativePlayerController(
            context = context,
            episodeId = episodeId,
            origin = origin,
            api = api,
            sessionHeaders = sessionHeaders,
        )
    }
    val ui by controller.state.collectAsState()

    var controlsVisible by remember { mutableStateOf(true) }
    var audioMenuOpen by remember { mutableStateOf(false) }
    var subtitleMenuOpen by remember { mutableStateOf(false) }
    var learningState by remember { mutableStateOf<LearningPlaybackState?>(null) }
    var scrubValue by remember { mutableFloatStateOf(-1f) }

    LaunchedEffect(controller) {
        controller.start()
    }

    LaunchedEffect(controlsVisible, ui.isPlaying, learningState) {
        if (controlsVisible && ui.isPlaying && learningState == null) {
            kotlinx.coroutines.delay(design.controlsAutoHideMs)
            controlsVisible = false
        }
    }

    DisposableEffect(controller) {
        onDispose {
            controller.close()
        }
    }

    DisposableEffect(activity, controller) {
        if (activity == null) {
            onDispose { }
        } else {
            val observer = LifecycleEventObserver { _, event ->
                if (event == Lifecycle.Event.ON_STOP) {
                    controller.onBackgrounded()
                }
            }
            activity.lifecycle.addObserver(observer)
            onDispose {
                activity.lifecycle.removeObserver(observer)
            }
        }
    }

    fun closeLearningSheet() {
        val previous = learningState
        controller.clearSelectedTerm()
        learningState = null
        if (previous != null &&
            LearningSheetPolicy.shouldResumeOnClose(previous) &&
            !controller.player.isPlaying
        ) {
            controller.resumeAfterLearning()
        }
    }

    BackHandler {
        when {
            ui.selectedTerm != null -> controller.clearSelectedTerm()
            learningState != null -> closeLearningSheet()
            else -> scope.launch {
                controller.persistBeforeClose()
                onClose()
            }
        }
    }

    Surface(
        modifier = Modifier.fillMaxSize(),
        color = Color.Black,
    ) {
        Box(modifier = Modifier.fillMaxSize()) {
            AndroidView(
                factory = { viewContext: Context ->
                    PlayerView(viewContext).apply {
                        player = controller.player
                        useController = false
                        resizeMode = AspectRatioFrameLayout.RESIZE_MODE_FIT
                        setShutterBackgroundColor(android.graphics.Color.BLACK)
                        keepScreenOn = true
                    }
                },
                update = { it.player = controller.player },
                modifier = Modifier.fillMaxSize(),
            )

            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .pointerInput(controller) {
                        detectTapGestures(
                            onTap = {
                                controlsVisible = !controlsVisible
                            },
                            onDoubleTap = { offset ->
                                controlsVisible = true
                                if (offset.x < size.width / 2f) {
                                    controller.seekBy(-10_000)
                                } else {
                                    controller.seekBy(10_000)
                                }
                            },
                        )
                    },
            )

            ui.currentCue?.let { cue ->
                Surface(
                    color = design.subtitleBackground,
                    shape = RoundedCornerShape(design.controlRadiusDp.dp),
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .padding(
                            start = 16.dp,
                            end = 16.dp,
                            bottom = if (controlsVisible) 156.dp else 56.dp,
                        )
                        .clickable {
                            learningState = LearningSheetPolicy.open(ui.isPlaying)
                            if (ui.isPlaying) {
                                controller.pauseForLearning()
                            }
                            controlsVisible = true
                        },
                ) {
                    Text(
                        text = cue.text,
                        color = design.subtitleText,
                        fontSize = design.subtitlePreferredSp.sp,
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp),
                    )
                }
            }

            if (controlsVisible || !ui.isPlaying || learningState != null) {
                PlayerControls(
                    ui = ui,
                    design = design,
                    scrubValue = scrubValue,
                    onScrubValue = { scrubValue = it },
                    onScrubFinished = {
                        if (scrubValue >= 0f) {
                            controller.seekTo(scrubValue.toLong())
                        }
                        scrubValue = -1f
                    },
                    onClose = {
                        scope.launch {
                            controller.persistBeforeClose()
                            onClose()
                        }
                    },
                    onPlayPause = controller::togglePlayPause,
                    onBack10 = { controller.seekBy(-10_000) },
                    onForward10 = { controller.seekBy(10_000) },
                    audioMenuOpen = audioMenuOpen,
                    onAudioMenuOpen = { audioMenuOpen = it },
                    subtitleMenuOpen = subtitleMenuOpen,
                    onSubtitleMenuOpen = { subtitleMenuOpen = it },
                    onAudioTrack = controller::selectAudioTrack,
                    onSubtitleTrack = controller::selectSubtitleTrack,
                )
            }

            ui.storage?.let { storage ->
                StorageRecoveryOverlay(
                    modifier = Modifier.align(Alignment.Center),
                    availability = storage,
                    exhausted = ui.storageRetryExhausted,
                    wakeInProgress = ui.wakeInProgress,
                    design = design,
                    onRetry = controller::retryStorage,
                    onWake = controller::wakeStorage,
                )
            }

            if (ui.loading && ui.storage == null) {
                StatusOverlay(
                    modifier = Modifier.align(Alignment.Center),
                    title = "Starting player",
                    detail = "Loading AniLingo playback state…",
                    design = design,
                    showProgress = true,
                )
            }

            ui.updateRequired?.let {
                StatusOverlay(
                    modifier = Modifier.align(Alignment.Center),
                    title = "Update required",
                    detail = it,
                    design = design,
                    showProgress = false,
                    actionLabel = "Back",
                    onAction = onClose,
                )
            }

            ui.error?.let {
                StatusOverlay(
                    modifier = Modifier.align(Alignment.Center),
                    title = "Playback unavailable",
                    detail = it,
                    design = design,
                    showProgress = false,
                    actionLabel = "Try again",
                    onAction = controller::retryPlayback,
                    secondaryLabel = "Back",
                    onSecondary = onClose,
                )
            }

            if (learningState != null) {
                LearningSheet(
                    modifier = Modifier.align(Alignment.BottomCenter),
                    cue = ui.currentCue,
                    selectedTerm = ui.selectedTerm,
                    termLoading = ui.termLoading,
                    design = design,
                    onToken = controller::loadTerm,
                    onRepeat = controller::repeatCurrentCue,
                    onKnown = { controller.setSelectedTermState("known") },
                    onLearning = { controller.setSelectedTermState("learning") },
                    onBackFromTerm = controller::clearSelectedTerm,
                    onClose = ::closeLearningSheet,
                )
            }
        }
    }
}

@Composable
private fun PlayerControls(
    ui: NativePlayerUiState,
    design: MobilePlayerDesign,
    scrubValue: Float,
    onScrubValue: (Float) -> Unit,
    onScrubFinished: () -> Unit,
    onClose: () -> Unit,
    onPlayPause: () -> Unit,
    onBack10: () -> Unit,
    onForward10: () -> Unit,
    audioMenuOpen: Boolean,
    onAudioMenuOpen: (Boolean) -> Unit,
    subtitleMenuOpen: Boolean,
    onSubtitleMenuOpen: (Boolean) -> Unit,
    onAudioTrack: (MediaTrack) -> Unit,
    onSubtitleTrack: (MediaTrack?) -> Unit,
) {
    val bootstrap = ui.bootstrap
    val duration = ui.durationMs.coerceAtLeast(1)
    val sliderPosition = if (scrubValue >= 0f) {
        scrubValue
    } else {
        ui.positionMs.coerceIn(0, duration).toFloat()
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(design.overlay),
    ) {
        Row(
            modifier = Modifier
                .align(Alignment.TopCenter)
                .fillMaxWidth()
                .padding(12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            TextButton(onClick = onClose) {
                Text("Close", color = Color.White)
            }
            Spacer(Modifier.width(8.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = bootstrap?.episode?.animeTitle ?: "AniLingo",
                    color = Color.White,
                    style = MaterialTheme.typography.titleMedium,
                )
                Text(
                    text = bootstrap?.episode?.title ?: "Episode",
                    color = design.muted,
                    style = MaterialTheme.typography.bodySmall,
                )
            }
            Surface(
                color = design.sheet,
                shape = RoundedCornerShape(design.controlRadiusDp.dp),
            ) {
                Text(
                    text = when (ui.transport) {
                        PlaybackTransport.DIRECT -> "Direct"
                        PlaybackTransport.HLS_FALLBACK -> "Server"
                        PlaybackTransport.LIVE_MP4_FALLBACK -> "Server"
                        null -> "Checking"
                    },
                    color = design.subtitleText,
                    modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp),
                )
            }
        }

        Column(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .padding(16.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(formatTime(ui.positionMs), color = Color.White)
                Text(formatTime(ui.durationMs), color = design.muted)
            }

            Slider(
                value = sliderPosition,
                onValueChange = onScrubValue,
                onValueChangeFinished = onScrubFinished,
                valueRange = 0f..duration.toFloat(),
            )

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceEvenly,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                TextButton(onClick = onBack10) {
                    Text("-10s", color = Color.White)
                }
                Button(onClick = onPlayPause) {
                    Text(if (ui.isPlaying) "Pause" else "Play")
                }
                TextButton(onClick = onForward10) {
                    Text("+10s", color = Color.White)
                }

                Box {
                    TextButton(onClick = { onAudioMenuOpen(true) }) {
                        Text("Audio", color = Color.White)
                    }
                    DropdownMenu(
                        expanded = audioMenuOpen,
                        onDismissRequest = { onAudioMenuOpen(false) },
                    ) {
                        bootstrap?.audioTracks.orEmpty().forEach { track ->
                            DropdownMenuItem(
                                text = {
                                    Text(
                                        trackLabel(
                                            track,
                                            selected = track.id == ui.selectedAudioTrackId,
                                        ),
                                    )
                                },
                                onClick = {
                                    onAudioTrack(track)
                                    onAudioMenuOpen(false)
                                },
                            )
                        }
                    }
                }

                Box {
                    TextButton(onClick = { onSubtitleMenuOpen(true) }) {
                        Text("Subtitles", color = Color.White)
                    }
                    DropdownMenu(
                        expanded = subtitleMenuOpen,
                        onDismissRequest = { onSubtitleMenuOpen(false) },
                    ) {
                        DropdownMenuItem(
                            text = { Text("Off") },
                            onClick = {
                                onSubtitleTrack(null)
                                onSubtitleMenuOpen(false)
                            },
                        )
                        bootstrap?.subtitleTracks.orEmpty().forEach { track ->
                            DropdownMenuItem(
                                text = {
                                    Text(
                                        trackLabel(
                                            track,
                                            selected = track.id == ui.selectedSubtitleTrackId,
                                        ),
                                    )
                                },
                                onClick = {
                                    onSubtitleTrack(track)
                                    onSubtitleMenuOpen(false)
                                },
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun StorageRecoveryOverlay(
    modifier: Modifier = Modifier,
    availability: MediaAvailability,
    exhausted: Boolean,
    wakeInProgress: Boolean,
    design: MobilePlayerDesign,
    onRetry: () -> Unit,
    onWake: () -> Unit,
) {
    val detail = when (availability.state) {
        "file_missing" -> "The episode file is missing from the configured media storage."
        "source_unreachable" -> "The media storage cannot currently be read."
        "source_starting" -> "The NAS is starting. AniLingo is waiting for media storage."
        "source_offline" -> "The media storage is offline."
        else -> "AniLingo is waiting for media storage."
    }

    StatusOverlay(
        modifier = modifier,
        title = if (exhausted) "Storage still unavailable" else "Waiting for storage",
        detail = detail,
        design = design,
        showProgress = !exhausted &&
            StorageRecoveryPolicy.shouldAutomaticallyRetry(availability),
        actionLabel = "Try again",
        onAction = onRetry,
        secondaryLabel = if (StorageRecoveryPolicy.canOfferWake(availability)) {
            if (wakeInProgress) "Waking…" else "Wake NAS"
        } else {
            null
        },
        onSecondary = if (StorageRecoveryPolicy.canOfferWake(availability) && !wakeInProgress) {
            onWake
        } else {
            null
        },
    )
}

@Composable
private fun StatusOverlay(
    modifier: Modifier = Modifier,
    title: String,
    detail: String,
    design: MobilePlayerDesign,
    showProgress: Boolean,
    actionLabel: String? = null,
    onAction: (() -> Unit)? = null,
    secondaryLabel: String? = null,
    onSecondary: (() -> Unit)? = null,
) {
    Surface(
        color = design.sheet,
        shape = RoundedCornerShape(design.sheetRadiusDp.dp),
        modifier = modifier.padding(24.dp),
    ) {
        Column(
            modifier = Modifier.padding(20.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            if (showProgress) {
                CircularProgressIndicator()
            }
            Text(
                text = title,
                color = design.subtitleText,
                style = MaterialTheme.typography.titleLarge,
            )
            Text(
                text = detail,
                color = design.muted,
                style = MaterialTheme.typography.bodyMedium,
            )
            if (actionLabel != null && onAction != null) {
                Button(onClick = onAction) {
                    Text(actionLabel)
                }
            }
            if (secondaryLabel != null && onSecondary != null) {
                TextButton(onClick = onSecondary) {
                    Text(secondaryLabel, color = design.subtitleText)
                }
            }
        }
    }
}

@Composable
private fun LearningSheet(
    modifier: Modifier = Modifier,
    cue: SubtitleCue?,
    selectedTerm: de.juloc.anilingo.core.model.TermDetail?,
    termLoading: Boolean,
    design: MobilePlayerDesign,
    onToken: (String) -> Unit,
    onRepeat: () -> Unit,
    onKnown: () -> Unit,
    onLearning: () -> Unit,
    onBackFromTerm: () -> Unit,
    onClose: () -> Unit,
) {
    Surface(
        color = design.sheet,
        shape = RoundedCornerShape(
            topStart = design.sheetRadiusDp.dp,
            topEnd = design.sheetRadiusDp.dp,
        ),
        modifier = modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(18.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = if (selectedTerm == null) "Learn this line" else "Word",
                    color = design.subtitleText,
                    style = MaterialTheme.typography.titleMedium,
                    modifier = Modifier.weight(1f),
                )
                if (selectedTerm != null) {
                    TextButton(onClick = onBackFromTerm) {
                        Text("Sentence")
                    }
                }
                TextButton(onClick = onClose) {
                    Text("Close")
                }
            }

            cue?.let {
                Text(
                    text = it.text,
                    color = design.subtitleText,
                    fontSize = 22.sp,
                )

                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(4.dp),
                ) {
                    it.tokens.forEach { token ->
                        val termId = token.termId
                        Text(
                            text = token.surface,
                            color = if (termId != null) design.focus else design.muted,
                            modifier = if (termId != null) {
                                Modifier
                                    .background(
                                        color = design.subtitleBackground,
                                        shape = RoundedCornerShape(design.controlRadiusDp.dp),
                                    )
                                    .clickable { onToken(termId) }
                                    .padding(horizontal = 8.dp, vertical = 6.dp)
                            } else {
                                Modifier.padding(horizontal = 2.dp, vertical = 6.dp)
                            },
                        )
                    }
                }
            }

            if (termLoading) {
                CircularProgressIndicator()
            }

            selectedTerm?.let { term ->
                Text(
                    text = term.canonical,
                    color = design.focus,
                    fontSize = 28.sp,
                )
                term.reading?.let {
                    Text(text = it, color = design.subtitleText)
                }
                term.meaning?.let {
                    Text(text = it, color = design.subtitleText)
                }
                Text(
                    text = "Learning state: ${term.state}",
                    color = design.muted,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(onClick = onLearning) {
                        Text("Learning")
                    }
                    Button(onClick = onKnown) {
                        Text("Known")
                    }
                }
            }

            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = onRepeat) {
                    Text("Repeat line")
                }
            }
        }
    }
}

private fun trackLabel(
    track: MediaTrack,
    selected: Boolean,
): String {
    val name = track.title?.takeIf { it.isNotBlank() }
        ?: track.language?.takeIf { it.isNotBlank() }
        ?: "Track ${track.streamIndex}"
    return if (selected) "✓ $name" else name
}

private fun formatTime(milliseconds: Long): String {
    val totalSeconds = (milliseconds.coerceAtLeast(0) / 1000)
    val minutes = totalSeconds / 60
    val seconds = totalSeconds % 60
    return "%d:%02d".format(minutes, seconds)
}
