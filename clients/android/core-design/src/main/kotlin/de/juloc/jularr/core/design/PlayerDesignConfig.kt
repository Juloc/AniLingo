package de.juloc.jularr.core.design

import android.content.Context
import android.graphics.Color
import org.json.JSONObject

data class PlayerDesignConfig(
    val overlayColor: Int,
    val sheetColor: Int,
    val subtitleTextColor: Int,
    val subtitleBackgroundColor: Int,
    val accentColor: Int,
    val mutedColor: Int,
    val focusColor: Int,
    val controlRadiusDp: Int,
    val sheetRadiusDp: Int,
    val spacingSmallDp: Int,
    val spacingMediumDp: Int,
    val spacingLargeDp: Int,
    val touchControlSizeDp: Int,
    val tvControlSizeDp: Int,
    val subtitlePreferredSp: Int,
    val controlsAutoHideMs: Long,
)

object PlayerDesignConfigLoader {
    fun load(context: Context): PlayerDesignConfig {
        val json = context.assets
            .open(PlayerDesignAssets.Tokens)
            .bufferedReader()
            .use { JSONObject(it.readText()) }

        val colors = json.getJSONObject("colors")
        val radius = json.getJSONObject("radiusDp")
        val spacing = json.getJSONObject("spacingDp")
        val controlSize = json.getJSONObject("controlSizeDp")
        val typography = json.getJSONObject("typographySp")
        val timing = json.getJSONObject("timingMs")

        return PlayerDesignConfig(
            overlayColor = colors.color("overlay"),
            sheetColor = colors.color("sheet"),
            subtitleTextColor = colors.color("subtitleText"),
            subtitleBackgroundColor = colors.color("subtitleBackground"),
            accentColor = colors.color("accent"),
            mutedColor = colors.color("muted"),
            focusColor = colors.color("focus"),
            controlRadiusDp = radius.getInt("control"),
            sheetRadiusDp = radius.getInt("sheet"),
            spacingSmallDp = spacing.getInt("sm"),
            spacingMediumDp = spacing.getInt("md"),
            spacingLargeDp = spacing.getInt("lg"),
            touchControlSizeDp = controlSize.getInt("touch"),
            tvControlSizeDp = controlSize.getInt("tv"),
            subtitlePreferredSp = typography.getInt("subtitlePreferred"),
            controlsAutoHideMs = timing.getLong("controlsAutoHide"),
        )
    }

    private fun JSONObject.color(name: String): Int =
        Color.parseColor(getString(name))
}
