package de.juloc.jularr.tv

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Surface
import androidx.tv.material3.Text

/**
 * Watchlist is one of the four required sidebar destinations (#522), but the v1 client
 * API (docs/ANDROID_CLIENTS.md §4) has no watchlist endpoint today — `WatchlistStore` /
 * `WatchlistModels.cs` only back the Razor `/Watchlist` page, not `/api/client/v1`. Rather
 * than invent that server surface in this Android-only change, this shows an honest empty
 * state; see the PR description for the follow-up server issue that adds
 * `GET /api/client/v1/watchlist`.
 */
@Composable
fun TvWatchlistScreen() {
    Surface(modifier = Modifier.fillMaxSize()) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(48.dp),
            contentAlignment = Alignment.Center,
        ) {
            Column(
                modifier = Modifier.width(620.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp),
            ) {
                Text(
                    stringResource(R.string.tv_watchlist_title),
                    style = MaterialTheme.typography.headlineLarge,
                )
                Text(
                    stringResource(R.string.tv_watchlist_unavailable),
                    style = MaterialTheme.typography.bodyLarge,
                )
            }
        }
    }
}
