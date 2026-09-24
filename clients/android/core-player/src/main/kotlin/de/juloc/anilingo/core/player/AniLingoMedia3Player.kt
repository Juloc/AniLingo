package de.juloc.anilingo.core.player

import android.content.Context
import android.net.Uri
import androidx.media3.common.AudioAttributes
import androidx.media3.common.MediaItem
import androidx.media3.datasource.DefaultHttpDataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.session.MediaSession
import java.io.Closeable

class AniLingoMedia3Player(context: Context) : Closeable {
    private val httpDataSourceFactory = DefaultHttpDataSource.Factory()
        .setAllowCrossProtocolRedirects(false)

    private val mediaSourceFactory = DefaultMediaSourceFactory(context)
        .setDataSourceFactory(httpDataSourceFactory)

    val player: ExoPlayer = ExoPlayer.Builder(context)
        .setMediaSourceFactory(mediaSourceFactory)
        .build()
        .also {
            it.setAudioAttributes(AudioAttributes.DEFAULT, true)
        }

    val mediaSession: MediaSession = MediaSession.Builder(context, player).build()

    fun open(
        uri: Uri,
        startPositionMs: Long = 0,
        playWhenReady: Boolean = true,
        requestHeaders: Map<String, String> = emptyMap(),
    ) {
        httpDataSourceFactory.setDefaultRequestProperties(requestHeaders)
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
