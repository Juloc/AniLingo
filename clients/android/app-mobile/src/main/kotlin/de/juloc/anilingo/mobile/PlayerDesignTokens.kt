package de.juloc.anilingo.mobile

import android.content.Context
import android.graphics.Color as AndroidColor
import androidx.compose.ui.graphics.Color
import de.juloc.anilingo.core.design.PlayerDesignAssets
import org.json.JSONObject

data class MobilePlayerDesign(
    val overlay: Color,
    val sheet: Color,
    val subtitleText: Color,
    val subtitleBackground: Color,
    val accent: Color,
    val muted: Color,
    val focus: Color,
    val controlRadiusDp: Int,
    val sheetRadiusDp: Int,
    val spacingSmallDp: Int,
    val spacingMediumDp: Int,
    val spacingLargeDp: Int,
    val subtitlePreferredSp: Int,
    val controlsAutoHideMs: Long,
)

object MobilePlayerDesignLoader {
    fun load(context: Context): MobilePlayerDesign {
        val json = context.assets
            .open(PlayerDesignAssets.Tokens)
            .bufferedReader()
            .use { JSONObject(it.readText()) }

        val colors = json.getJSONObject("colors")
        val radius = json.getJSONObject("radiusDp")
        val spacing = json.getJSONObject("spacingDp")
        val typography = json.getJSONObject("typographySp")
        val timing = json.getJSONObject("timingMs")

        return MobilePlayerDesign(
            overlay = colors.color("overlay"),
            sheet = colors.color("sheet"),
            subtitleText = colors.color("subtitleText"),
            subtitleBackground = colors.color("subtitleBackground"),
            accent = colors.color("accent"),
            muted = colors.color("muted"),
            focus = colors.color("focus"),
            controlRadiusDp = radius.getInt("control"),
            sheetRadiusDp = radius.getInt("sheet"),
            spacingSmallDp = spacing.getInt("sm"),
            spacingMediumDp = spacing.getInt("md"),
            spacingLargeDp = spacing.getInt("lg"),
            subtitlePreferredSp = typography.getInt("subtitlePreferred"),
            controlsAutoHideMs = timing.getLong("controlsAutoHide"),
        )
    }

    private fun JSONObject.color(name: String): Color =
        Color(AndroidColor.parseColor(getString(name)))
}
