package de.juloc.anilingo.tv

import android.view.SurfaceView
import androidx.activity.compose.BackHandler
import androidx.annotation.OptIn
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.tv.material3.Button
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Surface
import androidx.tv.material3.Text
import de.juloc.anilingo.core.model.MediaTrack
import de.juloc.anilingo.core.model.SubtitleCue
import de.juloc.anilingo.core.player.AniLingoMedia3Player
import kotlinx.coroutines.delay

@OptIn(UnstableApi::class)
@Composable
fun TvPlayerScreen(
    player: AniLingoMedia3Player,
    episodeTitle: String,
    currentCue: SubtitleCue?,
    audioTracks: List<MediaTrack> = emptyList(),
    subtitleTracks: List<MediaTrack> = emptyList(),
    selectedAudioTrackId: String? = null,
    selectedSubtitleTrackId: String? = null,
    onSelectAudioTrack: (String) -> Unit = {},
    onSelectSubtitleTrack: (String?) -> Unit = {},
    onPositionChanged: (positionMs: Long, durationMs: Long, isPlaying: Boolean) -> Unit = { _, _, _ -> },
    onSeeked: (positionMs: Long, durationMs: Long, isPlaying: Boolean) -> Unit = { _, _, _ -> },
    onPlaybackFailure: (positionMs: Long) -> Unit = {},
    canOpenOnPhone: Boolean = false,
    onExit: () -> Unit,
    onSetTermState: (termId: String, state: String) -> Unit,
    onOpenOnPhone: (cueId: Long?, termId: String?) -> Unit,
    modifier: Modifier = Modifier,
) {
    var uiState by remember { mutableStateOf(TvPlayerUiState()) }
    var isPlaying by remember { mutableStateOf(player.player.isPlaying) }
    var positionMs by remember { mutableStateOf(player.player.currentPosition.coerceAtLeast(0)) }
    var durationMs by remember { mutableStateOf(player.player.duration.takeIf { it > 0 } ?: 0L) }

    DisposableEffect(player) {
        val listener = object : Player.Listener {
            override fun onIsPlayingChanged(value: Boolean) {
                isPlaying = value
                positionMs = player.player.currentPosition.coerceAtLeast(0)
                durationMs = player.player.duration.takeIf { it > 0 } ?: durationMs
                onPositionChanged(positionMs, durationMs, value)
            }

            override fun onPlaybackStateChanged(playbackState: Int) {
                positionMs = player.player.currentPosition.coerceAtLeast(0)
                durationMs = player.player.duration.takeIf { it > 0 } ?: durationMs
            }

            override fun onPositionDiscontinuity(
                oldPosition: Player.PositionInfo,
                newPosition: Player.PositionInfo,
                reason: Int,
            ) {
                positionMs = newPosition.positionMs.coerceAtLeast(0)
                durationMs = player.player.duration.takeIf { it > 0 } ?: durationMs
                onSeeked(positionMs, durationMs, player.player.isPlaying)
            }

            override fun onPlayerError(error: androidx.media3.common.PlaybackException) {
                onPlaybackFailure(player.player.currentPosition.coerceAtLeast(0))
            }
        }
        player.player.addListener(listener)
        onDispose { player.player.removeListener(listener) }
    }

    LaunchedEffect(player, isPlaying) {
        while (isPlaying) {
            val current = player.player.currentPosition.coerceAtLeast(0)
            val duration = player.player.duration.takeIf { it > 0 } ?: durationMs
            positionMs = current
            durationMs = duration
            onPositionChanged(current, duration, true)
            delay(500)
        }
    }

    fun apply(transition: TvPlayerTransition) {
        uiState = transition.state
        applyEffects(
            effects = transition.effects,
            player = player,
            onExit = onExit,
            cue = currentCue,
            focusedWordIndex = transition.state.focusedWordIndex,
            onOpenOnPhone = onOpenOnPhone,
        )
    }

    BackHandler {
        apply(TvPlayerInteraction.back(uiState))
    }

    Surface(
        modifier = modifier
            .fillMaxSize()
            .onPreviewKeyEvent { event ->
                if (event.type != KeyEventType.KeyDown) {
                    return@onPreviewKeyEvent false
                }

                val transition = when (event.key) {
                    Key.MediaPlayPause,
                    Key.MediaPlay,
                    Key.MediaPause,
                    -> TvPlayerInteraction.mediaPlayPause(uiState)

                    Key.DirectionCenter,
                    Key.Enter,
                    -> {
                        if ((uiState.controlsVisible &&
                                uiState.learningLayer == TvLearningLayer.CLOSED) ||
                            uiState.learningLayer == TvLearningLayer.WORD
                        ) {
                            return@onPreviewKeyEvent false
                        }
                        TvPlayerInteraction.ok(
                            uiState,
                            currentCue?.tokens?.size ?: 0,
                        )
                    }

                    Key.DirectionLeft -> {
                        if (uiState.controlsVisible &&
                            uiState.learningLayer == TvLearningLayer.CLOSED
                        ) {
                            return@onPreviewKeyEvent false
                        }
                        TvPlayerInteraction.left(
                            uiState,
                            currentCue?.tokens?.size ?: 0,
                        )
                    }

                    Key.DirectionRight -> {
                        if (uiState.controlsVisible &&
                            uiState.learningLayer == TvLearningLayer.CLOSED
                        ) {
                            return@onPreviewKeyEvent false
                        }
                        TvPlayerInteraction.right(
                            uiState,
                            currentCue?.tokens?.size ?: 0,
                        )
                    }

                    else -> return@onPreviewKeyEvent false
                }

                apply(transition)
                true
            },
    ) {
        Box(modifier = Modifier.fillMaxSize()) {
            VideoSurface(player)

            currentCue?.let { cue ->
                if (uiState.learningLayer == TvLearningLayer.CLOSED) {
                    Text(
                        text = cue.text,
                        modifier = Modifier
                            .align(Alignment.BottomCenter)
                            .padding(bottom = if (uiState.controlsVisible) 132.dp else 48.dp)
                            .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.82f))
                            .padding(horizontal = 20.dp, vertical = 10.dp),
                        style = MaterialTheme.typography.headlineSmall,
                    )
                }
            }

            if (uiState.controlsVisible &&
                uiState.learningLayer == TvLearningLayer.CLOSED
            ) {
                PlayerControls(
                    episodeTitle = episodeTitle,
                    isPlaying = isPlaying,
                    canLearn = currentCue != null,
                    positionMs = positionMs,
                    durationMs = durationMs,
                    audioTracks = audioTracks,
                    subtitleTracks = subtitleTracks,
                    selectedAudioTrackId = selectedAudioTrackId,
                    selectedSubtitleTrackId = selectedSubtitleTrackId,
                    onSelectAudioTrack = onSelectAudioTrack,
                    onSelectSubtitleTrack = onSelectSubtitleTrack,
                    onBackTen = {
                        player.player.seekTo(
                            (player.player.currentPosition - 10_000).coerceAtLeast(0),
                        )
                    },
                    onPlayPause = {
                        if (player.player.isPlaying) player.player.pause() else player.player.play()
                    },
                    onForwardTen = {
                        val duration = player.player.duration
                        val target = player.player.currentPosition + 10_000
                        player.player.seekTo(
                            if (duration > 0) target.coerceAtMost(duration) else target,
                        )
                    },
                    onLearn = {
                        apply(
                            TvPlayerInteraction.learnCurrentLine(
                                state = uiState,
                                isPlaying = player.player.isPlaying,
                                wordCount = currentCue?.tokens?.size ?: 0,
                            ),
                        )
                    },
                    modifier = Modifier.align(Alignment.BottomCenter),
                )
            }

            if (uiState.learningLayer != TvLearningLayer.CLOSED && currentCue != null) {
                LearningOverlay(
                    cue = currentCue,
                    layer = uiState.learningLayer,
                    focusedWordIndex = uiState.focusedWordIndex,
                    onFocusWord = { index ->
                        uiState = uiState.copy(focusedWordIndex = index)
                    },
                    onOpenWord = { index ->
                        uiState = uiState.copy(
                            learningLayer = TvLearningLayer.WORD,
                            focusedWordIndex = index,
                        )
                    },
                    onRepeatLine = {
                        player.player.seekTo(currentCue.startMs.toLong())
                        player.player.play()
                    },
                    onMarkKnown = {
                        currentCue.tokens
                            .getOrNull(uiState.focusedWordIndex)
                            ?.termId
                            ?.let { onSetTermState(it, "known") }
                    },
                    onAddToLearning = {
                        currentCue.tokens
                            .getOrNull(uiState.focusedWordIndex)
                            ?.termId
                            ?.let { onSetTermState(it, "learning") }
                    },
                    onOpenOnPhone = if (canOpenOnPhone) {
                        {
                            val termId = currentCue.tokens
                                .getOrNull(uiState.focusedWordIndex)
                                ?.termId
                            onOpenOnPhone(currentCue.id, termId)
                        }
                    } else {
                        null
                    },
                    modifier = Modifier.align(Alignment.Center),
                )
            }
        }
    }
}

@OptIn(UnstableApi::class)
@Composable
private fun VideoSurface(player: AniLingoMedia3Player) {
    val context = LocalContext.current
    val surfaceView = remember { SurfaceView(context) }

    DisposableEffect(player, surfaceView) {
        player.player.setVideoSurfaceView(surfaceView)
        onDispose { player.player.clearVideoSurfaceView(surfaceView) }
    }

    AndroidView(
        factory = { surfaceView },
        modifier = Modifier.fillMaxSize(),
    )
}

@Composable
private fun PlayerControls(
    episodeTitle: String,
    isPlaying: Boolean,
    canLearn: Boolean,
    positionMs: Long,
    durationMs: Long,
    audioTracks: List<MediaTrack>,
    subtitleTracks: List<MediaTrack>,
    selectedAudioTrackId: String?,
    selectedSubtitleTrackId: String?,
    onSelectAudioTrack: (String) -> Unit,
    onSelectSubtitleTrack: (String?) -> Unit,
    onBackTen: () -> Unit,
    onPlayPause: () -> Unit,
    onForwardTen: () -> Unit,
    onLearn: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.90f))
            .padding(horizontal = 40.dp, vertical = 20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(text = episodeTitle, style = MaterialTheme.typography.titleLarge)
            Text(
                text = "${formatTime(positionMs)} / ${formatTime(durationMs)}",
                style = MaterialTheme.typography.bodyMedium,
            )
        }

        val progress = if (durationMs > 0) {
            (positionMs.toFloat() / durationMs.toFloat()).coerceIn(0f, 1f)
        } else {
            0f
        }
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(5.dp)
                .background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.25f)),
        ) {
            Box(
                modifier = Modifier
                    .fillMaxWidth(progress)
                    .height(5.dp)
                    .background(MaterialTheme.colorScheme.primary),
            )
        }

        Row(
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Button(onClick = onBackTen) { Text("−10 s") }
            Button(onClick = onPlayPause) { Text(if (isPlaying) "Pause" else "Play") }
            Button(onClick = onForwardTen) { Text("+10 s") }
            if (canLearn) {
                Button(onClick = onLearn) { Text("Learn this line") }
            }
        }

        if (audioTracks.isNotEmpty() || subtitleTracks.isNotEmpty()) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                if (audioTracks.isNotEmpty()) {
                    Button(
                        onClick = {
                            nextTrackId(audioTracks, selectedAudioTrackId)
                                ?.let(onSelectAudioTrack)
                        },
                        modifier = Modifier.widthIn(max = 300.dp),
                    ) {
                        Text(
                            "Audio: ${trackLabel(audioTracks, selectedAudioTrackId, "Default")}",
                            maxLines = 1,
                        )
                    }
                }
                if (subtitleTracks.isNotEmpty()) {
                    Button(
                        onClick = {
                            onSelectSubtitleTrack(
                                nextSubtitleTrackId(
                                    subtitleTracks,
                                    selectedSubtitleTrackId,
                                ),
                            )
                        },
                        modifier = Modifier.widthIn(max = 340.dp),
                    ) {
                        Text(
                            "Subtitles: ${trackLabel(subtitleTracks, selectedSubtitleTrackId, "Off")}",
                            maxLines = 1,
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun LearningOverlay(
    cue: SubtitleCue,
    layer: TvLearningLayer,
    focusedWordIndex: Int,
    onFocusWord: (Int) -> Unit,
    onOpenWord: (Int) -> Unit,
    onRepeatLine: () -> Unit,
    onMarkKnown: () -> Unit,
    onAddToLearning: () -> Unit,
    onOpenOnPhone: (() -> Unit)?,
    modifier: Modifier = Modifier,
) {
    val focused = cue.tokens.getOrNull(focusedWordIndex)

    Column(
        modifier = modifier
            .fillMaxWidth(0.86f)
            .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.96f))
            .padding(32.dp),
        verticalArrangement = Arrangement.spacedBy(18.dp),
    ) {
        Text(
            text = cue.text,
            style = MaterialTheme.typography.headlineMedium,
        )

        LazyRow(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            itemsIndexed(cue.tokens) { index, token ->
                Button(
                    onClick = {
                        onFocusWord(index)
                        onOpenWord(index)
                    },
                ) {
                    Text(
                        if (index == focusedWordIndex) "› ${token.surface} ‹" else token.surface,
                    )
                }
            }
        }

        if (layer == TvLearningLayer.WORD && focused != null) {
            Spacer(Modifier.height(4.dp))
            Text(
                text = focused.canonical ?: focused.surface,
                style = MaterialTheme.typography.titleLarge,
            )
            focused.reading?.let { Text(it, style = MaterialTheme.typography.bodyLarge) }
            focused.meaning?.let { Text(it, style = MaterialTheme.typography.bodyLarge) }
            Text(
                text = "Learning: ${focused.state}",
                style = MaterialTheme.typography.bodyMedium,
            )
        }

        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Button(onClick = onRepeatLine) {
                Text("Repeat line")
            }
            if (layer == TvLearningLayer.WORD && focused?.termId != null) {
                Button(onClick = onMarkKnown) {
                    Text("Known")
                }
                Button(onClick = onAddToLearning) {
                    Text("Learning")
                }
            }
            if (onOpenOnPhone != null) {
                Button(onClick = onOpenOnPhone) {
                    Text("Open on phone")
                }
            }
        }
    }
}

@OptIn(UnstableApi::class)
private fun applyEffects(
    effects: List<TvPlayerEffect>,
    player: AniLingoMedia3Player,
    onExit: () -> Unit,
    cue: SubtitleCue?,
    focusedWordIndex: Int,
    onOpenOnPhone: (Long?, String?) -> Unit,
) {
    for (effect in effects) {
        when (effect) {
            TvPlayerEffect.TogglePlayback -> {
                if (player.player.isPlaying) player.player.pause() else player.player.play()
            }

            is TvPlayerEffect.SeekBy -> {
                val duration = player.player.duration
                val target = (player.player.currentPosition + effect.deltaMs).coerceAtLeast(0)
                player.player.seekTo(
                    if (duration > 0) target.coerceAtMost(duration) else target,
                )
            }

            TvPlayerEffect.PausePlayback -> player.player.pause()
            TvPlayerEffect.ResumePlayback -> player.player.play()
            TvPlayerEffect.ExitPlayer -> onExit()
            TvPlayerEffect.OpenOnPhone -> onOpenOnPhone(
                cue?.id,
                cue?.tokens?.getOrNull(focusedWordIndex)?.termId,
            )
        }
    }
}

private fun formatTime(valueMs: Long): String {
    val totalSeconds = valueMs.coerceAtLeast(0) / 1000
    val hours = totalSeconds / 3600
    val minutes = (totalSeconds % 3600) / 60
    val seconds = totalSeconds % 60
    return if (hours > 0) {
        "%d:%02d:%02d".format(hours, minutes, seconds)
    } else {
        "%d:%02d".format(minutes, seconds)
    }
}

private fun trackLabel(
    tracks: List<MediaTrack>,
    selectedId: String?,
    fallback: String,
): String {
    val track = tracks.firstOrNull { it.id == selectedId } ?: return fallback
    return track.title
        ?: track.language?.uppercase()
        ?: track.codec?.uppercase()
        ?: track.id
}

private fun nextTrackId(
    tracks: List<MediaTrack>,
    selectedId: String?,
): String? {
    if (tracks.isEmpty()) return null
    val index = tracks.indexOfFirst { it.id == selectedId }
    return tracks[(index + 1).mod(tracks.size)].id
}

private fun nextSubtitleTrackId(
    tracks: List<MediaTrack>,
    selectedId: String?,
): String? {
    if (tracks.isEmpty()) return null
    if (selectedId == null) return tracks.first().id
    val index = tracks.indexOfFirst { it.id == selectedId }
    if (index < 0 || index == tracks.lastIndex) return null
    return tracks[index + 1].id
}
