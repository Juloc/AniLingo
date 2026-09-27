package de.juloc.jularr.tv

import androidx.compose.foundation.border
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import de.juloc.jularr.core.design.PlayerDesignConfigLoader

/**
 * D-pad focus is the *only* input Android TV guarantees (docs/ANDROID_CLIENTS.md §9,
 * #522: "no hover-only controls"), so every focusable surface in the TV app must show a
 * clear, consistent focused state. Rather than invent a second color, this reuses the
 * canonical `colors.focus` token from core-design's player-tokens.json (the same value
 * the player's transport controls already focus-highlight with).
 */
@Composable
fun rememberTvFocusColor(): Color {
    val context = LocalContext.current
    return remember(context) {
        Color(PlayerDesignConfigLoader.load(context).focusColor)
    }
}

/**
 * Applies the canonical focus border when [focused], and reports focus changes for
 * [TvFocusMemory] via [onFocusChanged]. Shared by the sidebar, Home's search field and
 * filter chips, and every content card so the whole app has one focus language.
 */
fun Modifier.tvFocusIndication(
    focused: Boolean,
    color: Color,
    shape: Shape = RectangleShape,
): Modifier =
    this.border(
        width = 3.dp,
        color = if (focused) color else Color.Transparent,
        shape = shape,
    )

fun Modifier.reportFocus(onFocused: (Boolean) -> Unit): Modifier =
    this.onFocusChanged { onFocused(it.isFocused) }
