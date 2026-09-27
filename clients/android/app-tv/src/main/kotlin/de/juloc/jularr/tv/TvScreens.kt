package de.juloc.jularr.tv

import androidx.compose.foundation.background
import androidx.compose.foundation.border
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
import androidx.compose.foundation.layout.weight
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.tv.material3.Button
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Surface
import androidx.tv.material3.Text
import coil3.compose.AsyncImage
import coil3.network.NetworkHeaders
import coil3.network.httpHeaders
import coil3.request.ImageRequest
import de.juloc.jularr.core.model.AnimeDetail
import de.juloc.jularr.core.model.AnimeSummary
import de.juloc.jularr.core.model.ClientAccount
import de.juloc.jularr.core.model.ClientLibrary
import de.juloc.jularr.core.model.EpisodeSummary
import java.net.URI

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
        title = "Connect Jularr",
        description = "Enter the address of your Jularr server.",
    ) {
        TvInput(
            value = origin,
            onValueChange = {
                origin = it
                localError = null
            },
            label = "Server address",
            placeholder = "https://jularr.example",
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
            Text(if (busy) "Connecting…" else "Continue")
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
        title = "Sign in",
        description = serverOrigin,
    ) {
        TvInput(
            value = userName,
            onValueChange = { userName = it },
            label = "User name",
            placeholder = "Jularr user",
        )
        TvInput(
            value = password,
            onValueChange = { password = it },
            label = "Password",
            placeholder = "Password",
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
                Text(if (busy) "Signing in…" else "Sign in")
            }
            Button(
                enabled = !busy,
                onClick = onChangeServer,
            ) {
                Text("Change server")
            }
        }
    }
}

@Composable
fun TvLibraryScreen(
    account: ClientAccount,
    library: ClientLibrary,
    serverOrigin: String,
    requestHeaders: Map<String, String>,
    error: String?,
    onAnime: (AnimeSummary) -> Unit,
    onRefresh: () -> Unit,
    onSignOut: () -> Unit,
) {
    Surface(modifier = Modifier.fillMaxSize()) {
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 48.dp, vertical = 32.dp),
            verticalArrangement = Arrangement.spacedBy(22.dp),
        ) {
            item {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
                        Text("Library", style = MaterialTheme.typography.headlineLarge)
                        Text(
                            "Jularr · ${account.userName ?: account.role}",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.72f),
                        )
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Button(onClick = onRefresh) { Text("Refresh") }
                        Button(onClick = onSignOut) { Text("Sign out") }
                    }
                }
            }

            error?.let {
                item {
                    Text(
                        text = it,
                        color = MaterialTheme.colorScheme.error,
                        style = MaterialTheme.typography.bodyMedium,
                    )
                }
            }

            item {
                if (library.anime.isEmpty()) {
                    Text(
                        "No anime in the library yet.",
                        style = MaterialTheme.typography.bodyLarge,
                    )
                } else {
                    LazyRow(horizontalArrangement = Arrangement.spacedBy(18.dp)) {
                        items(
                            items = library.anime,
                            key = { it.id },
                        ) { anime ->
                            AnimeButton(
                                anime = anime,
                                serverOrigin = serverOrigin,
                                requestHeaders = requestHeaders,
                                onClick = { onAnime(anime) },
                            )
                        }
                    }
                }
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
                        Button(onClick = onBack) { Text("Back") }
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
                                "${anime.seasons.sumOf { it.episodes.size }} episodes",
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
                        text = "Season ${season.number}",
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
                Button(onClick = onBack) { Text("Back") }
                Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
                    Text(
                        text = anime.title,
                        style = MaterialTheme.typography.titleLarge,
                    )
                    Text(
                        text = "Season ${episode.seasonNumber} · Episode ${episode.number}",
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
                            text = if (episode.hasMedia) {
                                "Ready to play"
                            } else {
                                "Media unavailable"
                            },
                        )
                        if (episode.activeLearningSubtitleTrackId != null) {
                            TvInfoPill(text = "Japanese learning subtitles")
                        }
                        if (progress.isCompleted) {
                            TvInfoPill(text = "Watched")
                        }
                    }

                    if (progress.percent > 0 && !progress.isCompleted) {
                        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                            ) {
                                Text(
                                    "Continue at ${formatEpisodePosition(progress.positionMs)}",
                                    style = MaterialTheme.typography.bodyLarge,
                                )
                                Text(
                                    "${progress.percent}%",
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
                        text = "Vocabulary · ${learning.knownTerms} known · " +
                            "${learning.learningTerms} learning · ${learning.newTerms} new",
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
                                    busy -> "Loading…"
                                    progress.positionMs > 0 && !progress.isCompleted -> "Resume"
                                    else -> "Play"
                                },
                            )
                        }
                        Button(
                            enabled = !busy,
                            onClick = onBack,
                        ) {
                            Text("Episodes")
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun TvInfoPill(private fun TvInfoPill(text: String) {
    Box(
        modifier = Modifier
            .background(
                color = MaterialTheme.colorScheme.surface,
                shape = MaterialTheme.shapes.small,
            )
            .padding(horizontal = 12.dp, vertical = 8.dp),
    ) {
        Text(
            text = text,
            style = MaterialTheme.typography.labelLarge,
        )
    }
}

private fun formatEpisodePosition(valueMs: Long): String {
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

@Composable
private fun AnimeButton(
    anime: AnimeSummary,
    serverOrigin: String,
    requestHeaders: Map<String, String>,
    onClick: () -> Unit,
) {
    Button(
        onClick = onClick,
        modifier = Modifier
            .width(210.dp)
            .height(330.dp),
    ) {
        Column(
            modifier = Modifier.fillMaxSize(),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            TvArtwork(
                url = anime.coverImageUrl,
                serverOrigin = serverOrigin,
                requestHeaders = requestHeaders,
                contentDescription = anime.title,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(225.dp)
                    .clip(MaterialTheme.shapes.small),
            )
            Text(
                text = anime.title,
                style = MaterialTheme.typography.titleMedium,
                maxLines = 2,
            )
            Text(
                text = buildString {
                    anime.seasonYear?.let { append(it).append(" · ") }
                    anime.format?.takeIf { it.isNotBlank() }?.let { append(it).append(" · ") }
                    append(anime.episodeCount).append(" ep.")
                },
                style = MaterialTheme.typography.bodySmall,
            )
        }
    }
}

@Composable
private fun EpisodeButton(
    episode: EpisodeSummary,
    onClick: () -> Unit,
) {
    Button(
        enabled = episode.hasMedia,
        onClick = onClick,
        modifier = Modifier
            .width(240.dp)
            .height(112.dp),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(5.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(
                    "E${episode.number}",
                    style = MaterialTheme.typography.titleMedium,
                )
                if (episode.hasJapaneseLearningSubtitle) {
                    Text(
                        "日本語",
                        style = MaterialTheme.typography.labelMedium,
                    )
                }
            }
            Text(
                episode.title,
                maxLines = 2,
                style = MaterialTheme.typography.bodyMedium,
            )
            if (!episode.hasMedia) {
                Text(
                    "Media unavailable",
                    style = MaterialTheme.typography.bodySmall,
                )
            }
        }
    }
}

private data class TvArtworkSource(
    val url: String,
    val authenticated: Boolean,
)

@Composable
private fun TvArtwork(
    url: String?,
    serverOrigin: String,
    requestHeaders: Map<String, String>,
    contentDescription: String?,
    modifier: Modifier = Modifier,
    contentScale: ContentScale = ContentScale.Crop,
) {
    val context = LocalContext.current
    val source = remember(url, serverOrigin) {
        resolveArtworkSource(serverOrigin, url)
    }

    Box(
        modifier = modifier.background(MaterialTheme.colorScheme.surfaceVariant),
        contentAlignment = Alignment.Center,
    ) {
        if (source == null) {
            Text(
                text = contentDescription
                    ?.trim()
                    ?.take(1)
                    ?.uppercase()
                    .orEmpty(),
                style = MaterialTheme.typography.headlineLarge,
                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.45f),
            )
            return@Box
        }

        val request = remember(source, requestHeaders) {
            val builder = ImageRequest.Builder(context)
                .data(source.url)

            if (source.authenticated && requestHeaders.isNotEmpty()) {
                val headers = NetworkHeaders.Builder().also { network ->
                    requestHeaders.forEach { (name, value) ->
                        network.set(name, value)
                    }
                }.build()
                builder.httpHeaders(headers)
            }

            builder.build()
        }

        AsyncImage(
            model = request,
            contentDescription = contentDescription,
            modifier = Modifier.fillMaxSize(),
            contentScale = contentScale,
        )
    }
}

private fun resolveArtworkSource(
    serverOrigin: String,
    rawUrl: String?,
): TvArtworkSource? {
    val value = rawUrl?.trim()?.takeIf { it.isNotEmpty() } ?: return null
    return runCatching {
        val candidate = URI(value)
        val base = serverOrigin
            .trim()
            .takeIf { it.isNotEmpty() }
            ?.let { URI(it.trimEnd('/') + "/") }

        val resolved = when {
            candidate.isAbsolute -> candidate
            base != null -> base.resolve(candidate)
            else -> return null
        }

        TvArtworkSource(
            url = resolved.toString(),
            authenticated = base != null && sameOrigin(base, resolved),
        )
    }.getOrNull()
}

private fun sameOrigin(
    left: URI,
    right: URI,
): Boolean =
    left.scheme.equals(right.scheme, ignoreCase = true) &&
        left.host.equals(right.host, ignoreCase = true) &&
        effectivePort(left) == effectivePort(right)

private fun effectivePort(uri: URI): Int =
    when {
        uri.port >= 0 -> uri.port
        uri.scheme.equals("https", ignoreCase = true) -> 443
        uri.scheme.equals("http", ignoreCase = true) -> 80
        else -> -1
}

@Composable
@Composable
private fun TvCenteredPanel(private fun TvCenteredPanel(
    title: String,
    description: String,
    content: @Composable () -> Unit,
) {
    Surface(modifier = Modifier.fillMaxSize()) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(48.dp),
            contentAlignment = Alignment.Center,
        ) {
            Column(
                modifier = Modifier.width(620.dp),
                verticalArrangement = Arrangement.spacedBy(18.dp),
            ) {
                Text(title, style = MaterialTheme.typography.headlineLarge)
                Text(description, style = MaterialTheme.typography.bodyLarge)
                content()
            }
        }
    }
}

@Composable
private fun TvInput(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    placeholder: String,
    password: Boolean = false,
) {
    var focused by remember { mutableStateOf(false) }

    Column(verticalArrangement = Arrangement.spacedBy(7.dp)) {
        Text(label, style = MaterialTheme.typography.labelLarge)
        BasicTextField(
            value = value,
            onValueChange = onValueChange,
            singleLine = true,
            visualTransformation = if (password) {
                PasswordVisualTransformation()
            } else {
                VisualTransformation.None
            },
            textStyle = TextStyle(
                color = MaterialTheme.colorScheme.onSurface,
                fontSize = 20.sp,
            ),
            cursorBrush = SolidColor(MaterialTheme.colorScheme.primary),
            modifier = Modifier
                .fillMaxWidth()
                .onFocusChanged { focused = it.isFocused }
                .border(
                    width = if (focused) 3.dp else 1.dp,
                    color = if (focused) {
                        MaterialTheme.colorScheme.primary
                    } else {
                        MaterialTheme.colorScheme.onSurface.copy(alpha = 0.35f)
                    },
                    shape = MaterialTheme.shapes.small,
                )
                .background(
                    color = MaterialTheme.colorScheme.surfaceVariant,
                    shape = MaterialTheme.shapes.small,
                )
                .padding(horizontal = 16.dp, vertical = 14.dp),
            decorationBox = { inner ->
                if (value.isEmpty()) {
                    Text(
                        placeholder,
                        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.5f),
                    )
                }
                inner()
            },
        )
    }
}
