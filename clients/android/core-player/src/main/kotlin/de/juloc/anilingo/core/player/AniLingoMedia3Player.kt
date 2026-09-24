package de.juloc.anilingo.core.player

import android.content.Context
import android.net.Uri
import androidx.media3.common.MediaItem
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.session.MediaSession
import java.io.Closeable

class AniLingoMedia3Player(context: Context) : Closeable {
    val player: ExoPlayer = ExoPlayer.Builder(context).build()
    val mediaSession: MediaSession = MediaSession.Builder(context, player).build()

    fun open(
        uri: Uri,
        startPositionMs: Long = 0,
        playWhenReady: Boolean = true,
    ) {
        player.setMediaItem(MediaItem.fromUri(uri))
        player.prepare()

        if (startPositionMs > 0) {
            player.seekTo(startPositionMs)
        }

        player.playWhenReady = playWhenReady
    }

    override fun close() {
        mediaSession.release()
        player.release()
    }
}
