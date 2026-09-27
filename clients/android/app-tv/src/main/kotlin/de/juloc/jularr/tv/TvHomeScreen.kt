package de.juloc.jularr.tv

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Button
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Surface
import androidx.tv.material3.Text
import de.juloc.jularr.core.model.AnimeSummary
import de.juloc.jularr.core.model.ClientLibrary
import de.juloc.jularr.core.model.ContinueWatchingItem

/**
 * Home is the TV app's single primary browsing surface (#522, "Correction: TV search
 * lives inside Home"): a search field at the very top, a content-type filter beneath it,
 * then content rows only — no hero banner, no filler copy. Activating the search field
 * opens [TvSearchScreen] (full browse/search); everything else here is direct content
 * rows (Continue Watching, then the library) fed straight from the data the app already
 * loaded at sign-in.
 */
@Composable
fun TvHomeScreen(
    library: ClientLibrary,
    continueWatching: List<ContinueWatchingItem>,
    serverOrigin: String,
    requestHeaders: Map<String, String>,
    error: String?,
    focusMemory: TvFocusMemory,
    onOpenSearch: () -> Unit,
    onAnime: (AnimeSummary) -> Unit,
    onContinueWatching: (ContinueWatchingItem) -> Unit,
) {
    var filter by remember { mutableStateOf(TvContentFilter.ALL) }
    val focusColor = rememberTvFocusColor()
    var searchFieldFocused by remember { mutableStateOf(false) }

    Surface(modifier = Modifier.fillMaxSize()) {
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 48.dp, vertical = 32.dp),
            verticalArrangement = Arrangement.spacedBy(22.dp),
        ) {
            item {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .background(
                            MaterialTheme.colorScheme.surfaceVariant,
                            MaterialTheme.shapes.large,
                        )
                        .tvFocusIndication(searchFieldFocused, focusColor, MaterialTheme.shapes.large)
                        .clickable(onClick = onOpenSearch)
                        .reportFocus { focused ->
                            searchFieldFocused = focused
                            if (focused) {
                                focusMemory.remember("home", "search-field")
                            }
                        }
                        .padding(horizontal = 20.dp, vertical = 16.dp),
                ) {
                    Text(
                        text = stringResource(R.string.tv_home_search_placeholder),
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.72f),
                    )
                }
            }

            item {
                TvFilterChipRow(
                    selected = filter,
                    focusMemory = focusMemory,
                    screenKey = "home",
                    onSelect = { filter = it },
                )
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

            if (continueWatching.isNotEmpty()) {
                item {
                    Text(
                        stringResource(R.string.tv_home_row_continue_watching),
                        style = MaterialTheme.typography.titleLarge,
                    )
                }
                item {
                    LazyRow(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                        items(items = continueWatching, key = { it.episodeId }) { row ->
                            ContinueWatchingCard(
                                item = row,
                                serverOrigin = serverOrigin,
                                requestHeaders = requestHeaders,
                                onClick = { onContinueWatching(row) },
                            )
                        }
                    }
                }
            }

            item {
                Text(
                    stringResource(R.string.tv_home_row_anime),
                    style = MaterialTheme.typography.titleLarge,
                )
            }
            item {
                if (library.anime.isEmpty()) {
                    Text(
                        stringResource(R.string.tv_home_empty),
                        style = MaterialTheme.typography.bodyLarge,
                    )
                } else {
                    LazyRow(horizontalArrangement = Arrangement.spacedBy(18.dp)) {
                        items(items = library.anime, key = { it.id }) { anime ->
                            var focused by remember(anime.id) { mutableStateOf(false) }
                            AnimeButton(
                                anime = anime,
                                serverOrigin = serverOrigin,
                                requestHeaders = requestHeaders,
                                focused = focused,
                                focusColor = focusColor,
                                onFocusChanged = { isFocused ->
                                    focused = isFocused
                                    if (isFocused) {
                                        focusMemory.remember("home", "anime:${anime.id}")
                                    }
                                },
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
private fun ContinueWatchingCard(
    item: ContinueWatchingItem,
    serverOrigin: String,
    requestHeaders: Map<String, String>,
    onClick: () -> Unit,
) {
    val focusColor = rememberTvFocusColor()
    var focused by remember(item.episodeId) { mutableStateOf(false) }

    Button(
        onClick = onClick,
        modifier = Modifier
            .width(280.dp)
            .height(190.dp)
            .tvFocusIndication(focused, focusColor, MaterialTheme.shapes.small)
            .reportFocus { focused = it },
    ) {
        Column(
            modifier = Modifier.fillMaxSize(),
            verticalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            TvArtwork(
                url = item.coverImageUrl,
                serverOrigin = serverOrigin,
                requestHeaders = requestHeaders,
                contentDescription = item.animeTitle,
                modifier = Modifier
                    .fillMaxWidth()
                    .aspectRatio(16f / 9f)
                    .clip(MaterialTheme.shapes.small),
            )
            Text(
                text = item.animeTitle,
                style = MaterialTheme.typography.titleMedium,
                maxLines = 1,
            )
            Text(
                text = stringResource(
                    R.string.tv_episode_season_and_number,
                    item.seasonNumber,
                    item.episodeNumber,
                ),
                style = MaterialTheme.typography.bodySmall,
            )
        }
    }
}
