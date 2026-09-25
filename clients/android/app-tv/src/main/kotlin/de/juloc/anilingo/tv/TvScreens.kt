package de.juloc.anilingo.tv

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
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.tv.material3.Button
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Surface
import androidx.tv.material3.Text
import de.juloc.anilingo.core.model.AnimeDetail
import de.juloc.anilingo.core.model.AnimeSummary
import de.juloc.anilingo.core.model.ClientAccount
import de.juloc.anilingo.core.model.ClientLibrary
import de.juloc.anilingo.core.model.EpisodeSummary

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
        title = "Connect AniLingo",
        description = "Enter the address of your AniLingo server.",
    ) {
        TvInput(
            value = origin,
            onValueChange = {
                origin = it
                localError = null
            },
            label = "Server address",
            placeholder = "https://anilingo.example",
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
            placeholder = "AniLingo user",
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
    error: String?,
    onAnime: (AnimeSummary) -> Unit,
    onRefresh: () -> Unit,
    onSignOut: () -> Unit,
) {
    Surface(modifier = Modifier.fillMaxSize()) {
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 48.dp, vertical = 36.dp),
            verticalArrangement = Arrangement.spacedBy(24.dp),
        ) {
            item {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Text("AniLingo", style = MaterialTheme.typography.headlineMedium)
                        Text(
                            "Signed in as ${account.userName ?: account.role}",
                            style = MaterialTheme.typography.bodyMedium,
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
                Text("Library", style = MaterialTheme.typography.titleLarge)
            }

            item {
                if (library.anime.isEmpty()) {
                    Text(
                        "No anime in the library yet.",
                        style = MaterialTheme.typography.bodyLarge,
                    )
                } else {
                    LazyRow(horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                        items(
                            items = library.anime,
                            key = { it.id },
                        ) { anime ->
                            AnimeButton(anime, onClick = { onAnime(anime) })
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
    onEpisode: (EpisodeSummary) -> Unit,
    onBack: () -> Unit,
) {
    Surface(modifier = Modifier.fillMaxSize()) {
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 48.dp, vertical = 36.dp),
            verticalArrangement = Arrangement.spacedBy(22.dp),
        ) {
            item {
                Row(
                    horizontalArrangement = Arrangement.spacedBy(14.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Button(onClick = onBack) { Text("Back") }
                    Column {
                        Text(anime.title, style = MaterialTheme.typography.headlineMedium)
                        if (anime.localTitle != anime.title) {
                            Text(anime.localTitle, style = MaterialTheme.typography.bodyMedium)
                        }
                    }
                }
            }

            anime.description?.takeIf { it.isNotBlank() }?.let { description ->
                item {
                    Text(
                        text = description,
                        style = MaterialTheme.typography.bodyLarge,
                    )
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
private fun AnimeButton(
    anime: AnimeSummary,
    onClick: () -> Unit,
) {
    Button(
        onClick = onClick,
        modifier = Modifier
            .width(260.dp)
            .height(110.dp),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(anime.title, style = MaterialTheme.typography.titleMedium)
            Text(
                "${anime.episodeCount} episodes",
                style = MaterialTheme.typography.bodySmall,
            )
            anime.seasonYear?.let {
                Text(it.toString(), style = MaterialTheme.typography.bodySmall)
            }
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
            .width(210.dp)
            .height(92.dp),
    ) {
        Column {
            Text(
                "S${episode.seasonNumber} · E${episode.number}",
                style = MaterialTheme.typography.titleMedium,
            )
            Text(
                episode.title,
                maxLines = 2,
                style = MaterialTheme.typography.bodySmall,
            )
        }
    }
}

@Composable
private fun TvCenteredPanel(
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
