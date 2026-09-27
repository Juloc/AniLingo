package de.juloc.jularr.tv

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Button
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Surface
import androidx.tv.material3.Text
import de.juloc.jularr.core.model.AnimeDetail
import de.juloc.jularr.core.model.EpisodeSummary

@Composable
fun TvSetupScreen(
    initialOrigin: String,
    error: String?,
    busy: Boolean,
    onConnect: (String) -> Unit,
) {
    var origin by rememberSaveable(initialOrigin) { mutableStateOf(initialOrigin) }
    var localError by remember { mutableStateOf<String?>(null) }

    TvCenteredPanel(
        title = stringResource(R.string.tv_setup_title),
        description = stringResource(R.string.tv_setup_description),
    ) {
        TvInput(
            value = origin,
            onValueChange = {
                origin = it
                localError = null
            },
            label = stringResource(R.string.tv_setup_label_server),
            placeholder = stringResource(R.string.tv_setup_placeholder_server),
        )

        (localError ?: error)?.let {
            Text(
                text = it,
                color = MaterialTheme.colorScheme.error,
                style = MaterialTheme.typography.bodyMedium,
            )
        }

        Button(
            enabled = !busy,
            onClick = {
                val normalized = runCatching { TvServerOrigin.normalize(origin) }
                if (normalized.isFailure) {
                    localError = normalized.exceptionOrNull()?.message
                } else {
                    localError = null
                    onConnect(normalized.getOrThrow())
                }
            },
        ) {
            Text(
                if (busy) {
                    stringResource(R.string.tv_setup_button_connecting)
                } else {
                    stringResource(R.string.tv_setup_button_continue)
                },
            )
        }
    }
}

@Composable
fun TvLoginScreen(
    serverOrigin: String,
    error: String?,
    busy: Boolean,
    onLogin: (userName: String, password: String) -> Unit,
    onChangeServer: () -> Unit,
) {
    var userName by rememberSaveable { mutableStateOf("") }
    var password by rememberSaveable { mutableStateOf("") }

    TvCenteredPanel(
        title = stringResource(R.string.tv_login_title),
        description = serverOrigin,
    ) {
        TvInput(
            value = userName,
            onValueChange = { userName = it },
            label = stringResource(R.string.tv_login_label_username),
            placeholder = stringResource(R.string.tv_login_placeholder_username),
        )
        TvInput(
            value = password,
            onValueChange = { password = it },
            label = stringResource(R.string.tv_login_label_password),
            placeholder = stringResource(R.string.tv_login_placeholder_password),
            password = true,
        )

        error?.let {
            Text(
                text = it,
                color = MaterialTheme.colorScheme.error,
                style = MaterialTheme.typography.bodyMedium,
            )
        }

        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Button(
                enabled = !busy && userName.isNotBlank() && password.isNotEmpty(),
                onClick = { onLogin(userName.trim(), password) },
            ) {
                Text(
                    if (busy) {
                        stringResource(R.string.tv_login_button_signing_in)
                    } else {
                        stringResource(R.string.tv_login_button_sign_in)
                    },
                )
            }
            Button(
                enabled = !busy,
                onClick = onChangeServer,
            ) {
                Text(stringResource(R.string.tv_login_button_change_server))
            }
        }
    }
}

@Composable
fun TvAnimeScreen(
    anime: AnimeDetail,
    serverOrigin: String,
    requestHeaders: Map<String, String>,
    onEpisode: (EpisodeSummary) -> Unit,
    onBack: () -> Unit,
) {
    Surface(modifier = Modifier.fillMaxSize()) {
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 48.dp, vertical = 32.dp),
            verticalArrangement = Arrangement.spacedBy(24.dp),
        ) {
            item {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(24.dp),
                    verticalAlignment = Alignment.Top,
                ) {
                    Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                        Button(onClick = onBack) { Text(stringResource(R.string.tv_action_back)) }
                        TvArtwork(
                            url = anime.coverImageUrl,
                            serverOrigin = serverOrigin,
                            requestHeaders = requestHeaders,
                            contentDescription = anime.title,
                            modifier = Modifier
                                .width(170.dp)
                                .aspectRatio(2f / 3f)
                                .clip(MaterialTheme.shapes.medium),
                        )
                    }

                    Column(
                        modifier = Modifier.weight(1f),
                        verticalArrangement = Arrangement.spacedBy(10.dp),
                    ) {
                        Text(anime.title, style = MaterialTheme.typography.headlineLarge)
                        anime.nativeTitle?.takeIf { it.isNotBlank() }?.let {
                            Text(
                                text = it,
                                style = MaterialTheme.typography.titleMedium,
                                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.74f),
                            )
                        }
                        if (anime.localTitle != anime.title) {
                            Text(
                                anime.localTitle,
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.68f),
                            )
                        }

                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            anime.seasonYear?.let { TvInfoPill(it.toString()) }
                            anime.format?.takeIf { it.isNotBlank() }?.let { TvInfoPill(it) }
                            TvInfoPill(
                                stringResource(
                                    R.string.tv_anime_episode_count,
                                    anime.seasons.sumOf { it.episodes.size },
                                ),
                            )
                        }

                        anime.description?.takeIf { it.isNotBlank() }?.let { description ->
                            Text(
                                text = description,
                                style = MaterialTheme.typography.bodyLarge,
                                maxLines = 5,
                            )
                        }
                    }
                }
            }

            for (season in anime.seasons) {
                item {
                    Text(
                        text = stringResource(R.string.tv_anime_season_header, season.number),
                        style = MaterialTheme.typography.titleLarge,
                    )
                }
                item {
                    LazyRow(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        items(
                            items = season.episodes,
                            key = { it.id },
                        ) { episode ->
                            EpisodeButton(
                                episode = episode,
                                onClick = { onEpisode(episode) },
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun TvEpisodeScreen(
    anime: AnimeDetail,
    page: TvEpisodePageData,
    serverOrigin: String,
    requestHeaders: Map<String, String>,
    busy: Boolean,
    error: String?,
    onPlay: () -> Unit,
    onBack: () -> Unit,
) {
    val episode = page.detail
    val progress = page.progress
    val progressFraction = progress.percent.coerceIn(0, 100) / 100f

    Surface(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 52.dp, vertical = 34.dp),
            verticalArrangement = Arrangement.spacedBy(22.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(18.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Button(onClick = onBack) { Text(stringResource(R.string.tv_action_back)) }
                Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
                    Text(
                        text = anime.title,
                        style = MaterialTheme.typography.titleLarge,
                    )
                    Text(
                        text = stringResource(
                            R.string.tv_episode_season_and_number,
                            episode.seasonNumber,
                            episode.number,
                        ),
                        style = MaterialTheme.typography.bodyMedium,
                    )
                }
            }

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(28.dp),
                verticalAlignment = Alignment.Top,
            ) {
                TvArtwork(
                    url = anime.coverImageUrl,
                    serverOrigin = serverOrigin,
                    requestHeaders = requestHeaders,
                    contentDescription = anime.title,
                    modifier = Modifier
                        .width(180.dp)
                        .aspectRatio(2f / 3f)
                        .clip(MaterialTheme.shapes.medium),
                )

                Column(
                    modifier = Modifier
                        .weight(1f)
                        .background(
                            MaterialTheme.colorScheme.surfaceVariant,
                            MaterialTheme.shapes.medium,
                        )
                        .padding(28.dp),
                    verticalArrangement = Arrangement.spacedBy(16.dp),
                ) {
                    Text(
                        text = episode.title,
                        style = MaterialTheme.typography.headlineLarge,
                    )

                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        TvInfoPill(
                            text = stringResource(
                                if (episode.hasMedia) {
                                    R.string.tv_episode_status_ready
                                } else {
                                    R.string.tv_episode_status_media_unavailable
                                },
                            ),
                        )
                        if (episode.activeLearningSubtitleTrackId != null) {
                            TvInfoPill(text = stringResource(R.string.tv_episode_status_japanese_subtitles))
                        }
                        if (progress.isCompleted) {
                            TvInfoPill(text = stringResource(R.string.tv_episode_status_watched))
                        }
                    }

                    if (progress.percent > 0 && !progress.isCompleted) {
                        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                            ) {
                                Text(
                                    stringResource(
                                        R.string.tv_episode_continue_at,
                                        formatEpisodePosition(progress.positionMs),
                                    ),
                                    style = MaterialTheme.typography.bodyLarge,
                                )
                                Text(
                                    stringResource(R.string.tv_episode_progress_percent, progress.percent),
                                    style = MaterialTheme.typography.bodyMedium,
                                )
                            }
                            Box(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .height(6.dp)
                                    .background(
                                        MaterialTheme.colorScheme.onSurface.copy(alpha = 0.20f),
                                    ),
                            ) {
                                Box(
                                    modifier = Modifier
                                        .fillMaxWidth(progressFraction)
                                        .height(6.dp)
                                        .background(MaterialTheme.colorScheme.primary),
                                )
                            }
                        }
                    }

                    val learning = episode.learning
                    Text(
                        text = stringResource(
                            R.string.tv_episode_vocabulary,
                            learning.knownTerms,
                            learning.learningTerms,
                            learning.newTerms,
                        ),
                        style = MaterialTheme.typography.bodyMedium,
                    )

                    error?.let {
                        Text(
                            text = it,
                            color = MaterialTheme.colorScheme.error,
                            style = MaterialTheme.typography.bodyMedium,
                        )
                    }

                    Row(horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                        Button(
                            enabled = episode.hasMedia && !busy,
                            onClick = onPlay,
                        ) {
                            Text(
                                when {
                                    busy -> stringResource(R.string.tv_episode_loading)
                                    progress.positionMs > 0 && !progress.isCompleted ->
                                        stringResource(R.string.tv_episode_resume)
                                    else -> stringResource(R.string.tv_episode_play)
                                },
                            )
                        }
                        Button(
                            enabled = !busy,
                            onClick = onBack,
                        ) {
                            Text(stringResource(R.string.tv_episode_episodes_button))
                        }
                    }
                }
            }
        }
    }
}
