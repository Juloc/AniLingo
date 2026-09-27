package de.juloc.jularr.tv

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.selectable
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.Bookmark
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Home
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Icon
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text

private data class TvSidebarItem(
    val route: TvRoute,
    val labelRes: Int,
    val icon: ImageVector,
)

private val sidebarItems = listOf(
    TvSidebarItem(TvRoute.Home, R.string.tv_sidebar_home, Icons.Filled.Home),
    TvSidebarItem(TvRoute.Watchlist, R.string.tv_sidebar_watchlist, Icons.Filled.Bookmark),
    TvSidebarItem(TvRoute.Activity, R.string.tv_sidebar_activity, Icons.Filled.History),
    TvSidebarItem(TvRoute.Profile, R.string.tv_sidebar_profile, Icons.Filled.AccountCircle),
)

/**
 * The TV app's only top-level navigation surface (#522): Home, Watchlist, Activity,
 * Profile/Settings, and nothing else. Compact (icon-only) until a child gains focus, per
 * the epic's "icon sidebar can remain compact until focused" remote-UX note, then expands
 * to show labels. Remembers which entry was last focused via [focusMemory] so returning
 * from a pushed screen (Anime, Episode, Search, ...) restores the same sidebar focus.
 */
@Composable
fun TvSidebar(
    selected: TvRoute,
    focusMemory: TvFocusMemory,
    onSelect: (TvRoute) -> Unit,
) {
    val focusColor = rememberTvFocusColor()
    var focusedRoute by remember { mutableStateOf<TvRoute?>(null) }
    val expanded = focusedRoute != null
    val width by animateDpAsState(if (expanded) 220.dp else 88.dp, label = "sidebar-width")

    Column(
        modifier = Modifier
            .fillMaxHeight()
            .width(width)
            .background(MaterialTheme.colorScheme.surface)
            .padding(vertical = 24.dp, horizontal = 12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        for (item in sidebarItems) {
            val isSelected = item.route == selected
            val isFocused = focusedRoute == item.route
            val background by animateColorAsState(
                if (isSelected) {
                    MaterialTheme.colorScheme.primaryContainer
                } else {
                    MaterialTheme.colorScheme.surface
                },
                label = "sidebar-item-background",
            )

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(background, MaterialTheme.shapes.medium)
                    .tvFocusIndication(isFocused, focusColor, MaterialTheme.shapes.medium)
                    .selectable(selected = isSelected, onClick = { onSelect(item.route) })
                    .reportFocus { hasFocus ->
                        focusedRoute = if (hasFocus) {
                            focusMemory.remember(
                                "sidebar",
                                TvNavigation.screenKey(item.route),
                            )
                            item.route
                        } else if (focusedRoute == item.route) {
                            null
                        } else {
                            focusedRoute
                        }
                    }
                    .padding(horizontal = 14.dp, vertical = 14.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                Icon(imageVector = item.icon, contentDescription = stringResource(item.labelRes))
                if (expanded || isSelected) {
                    Text(
                        text = stringResource(item.labelRes),
                        style = MaterialTheme.typography.titleMedium,
                    )
                }
            }
        }
    }
}
