package de.juloc.anilingo.tv

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.annotation.OptIn
import androidx.activity.compose.setContent
import androidx.media3.common.util.UnstableApi
import androidx.tv.material3.MaterialTheme
import de.juloc.anilingo.core.api.HttpAniLingoClientApi
import de.juloc.anilingo.core.player.AniLingoMedia3Player

@OptIn(UnstableApi::class)
class TvMainActivity : ComponentActivity() {
    private lateinit var player: AniLingoMedia3Player

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val settings = TvServerSettings(applicationContext)
        val cookies = TvSessionCookieStore()
        val controller = TvAppController(settings) { origin ->
            HttpAniLingoClientApi(
                origin = origin,
                requestHeaders = cookies::requestHeaders,
                responseCookieSink = cookies::accept,
            )
        }

        player = AniLingoMedia3Player(applicationContext)

        setContent {
            MaterialTheme {
                TvAppHost(
                    controller = controller,
                    settings = settings,
                    cookies = cookies,
                    player = player,
                    onFinish = ::finish,
                )
            }
        }
    }

    override fun onDestroy() {
        if (::player.isInitialized) {
            player.close()
        }
        super.onDestroy()
    }
}
