package de.juloc.jularr.tv

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
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
import de.juloc.jularr.core.model.AnimeSummary
import de.juloc.jularr.core.model.EpisodeSummary
import java.net.URI

/**
 * Building blocks shared by every browse/detail screen (Home, Search, Anime, Episode).
 * Kept in one file so Home and Search render anime cards identically to the existing
 * Anime/Episode detail screens instead of drifting into a second visual language.
 */
@Composable
internal fun TvCenteredPanel(
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
internal fun TvInput(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    placeholder: String,
    password: Boolean = false,
    onFocusChanged: ((Boolean) -> Unit)? = null,
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
                .onFocusChanged {
                    focused = it.isFocused
                    onFocusChanged?.invoke(it.isFocused)
                }
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

/**
 * The All/Movies/TV/Anime content filter shared by Home and Search (#522). Only
 * [TvContentFilter.visible] entries are rendered; see that enum for why Movies/TV are
 * hidden today.
 */
@Composable
internal fun TvFilterChipRow(
    selected: TvContentFilter,
    focusMemory: TvFocusMemory,
    screenKey: String,
    onSelect: (TvContentFilter) -> Unit,
) {
    val focusColor = rememberTvFocusColor()
    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        for (filter in TvContentFilter.visible) {
            var focused by remember(filter) { mutableStateOf(false) }
            val isSelected = filter == selected

            Box(
                modifier = Modifier
                    .background(
                        color = if (isSelected) {
                            MaterialTheme.colorScheme.primary
                        } else {
                            MaterialTheme.colorScheme.surfaceVariant
                        },
                        shape = MaterialTheme.shapes.extraLarge,
                    )
                    .tvFocusIndication(focused, focusColor, MaterialTheme.shapes.extraLarge)
                    .selectable(
                        selected = isSelected,
                        onClick = { onSelect(filter) },
                    )
                    .reportFocus { hasFocus ->
                        focused = hasFocus
                        if (hasFocus) {
                            focusMemory.remember(screenKey, "filter:${filter.name}")
                        }
                    }
                    .padding(horizontal = 18.dp, vertical = 10.dp),
            ) {
                Text(
                    text = stringResource(filter.labelRes),
                    style = MaterialTheme.typography.labelLarge,
                    color = if (isSelected) {
                        MaterialTheme.colorScheme.onPrimary
                    } else {
                        MaterialTheme.colorScheme.onSurface
                    },
                )
            }
        }
    }
}

@Composable
internal fun TvInfoPill(text: String) {
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

internal fun formatEpisodePosition(valueMs: Long): String {
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
internal fun AnimeButton(
    anime: AnimeSummary,
    serverOrigin: String,
    requestHeaders: Map<String, String>,
    focused: Boolean,
    focusColor: Color,
    onFocusChanged: (Boolean) -> Unit,
    onClick: () -> Unit,
) {
    Button(
        onClick = onClick,
        modifier = Modifier
            .width(210.dp)
            .height(330.dp)
            .tvFocusIndication(focused, focusColor, MaterialTheme.shapes.small)
            .reportFocus(onFocusChanged),
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
internal fun EpisodeButton(
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
                    stringResource(R.string.tv_episode_number, episode.number),
                    style = MaterialTheme.typography.titleMedium,
                )
                if (episode.hasJapaneseLearningSubtitle) {
                    Text(
                        stringResource(R.string.tv_episode_japanese_label),
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
                    stringResource(R.string.tv_episode_status_media_unavailable),
                    style = MaterialTheme.typography.bodySmall,
                )
            }
        }
    }
}

internal data class TvArtworkSource(
    val url: String,
    val authenticated: Boolean,
)

@Composable
internal fun TvArtwork(
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

internal fun resolveArtworkSource(
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
