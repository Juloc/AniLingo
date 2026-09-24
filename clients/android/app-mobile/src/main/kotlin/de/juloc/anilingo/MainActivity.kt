package de.juloc.anilingo

import android.os.Bundle
import android.webkit.CookieManager
import android.webkit.WebView
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import de.juloc.anilingo.core.api.HttpAniLingoClientApi
import de.juloc.anilingo.mobile.AniLingoWebShell
import de.juloc.anilingo.mobile.MobileCompatibilityGate
import de.juloc.anilingo.mobile.MobileCompatibilityState
import de.juloc.anilingo.mobile.NativePlayerScreen
import de.juloc.anilingo.mobile.ServerOrigin
import de.juloc.anilingo.mobile.ServerSettings
import de.juloc.anilingo.mobile.WebSession
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class MainActivity : ComponentActivity() {
    private var webView: WebView? = null
    private var activeOrigin: String? = null
    private var restoredWebState: Bundle? = null
    private var restoredWebOrigin: String? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        restoredWebState = savedInstanceState?.getBundle(KeyWebViewState)
        restoredWebOrigin = savedInstanceState?.getString(KeyWebViewOrigin)

        setContent {
            MaterialTheme {
                AniLingoMobileApp(
                    restoredWebState = restoredWebState,
                    restoredWebOrigin = restoredWebOrigin,
                    onWebViewChanged = { webView = it },
                    onOriginChanged = { activeOrigin = it },
                    onFinish = ::finish,
                )
            }
        }
    }

    override fun onSaveInstanceState(outState: Bundle) {
        val state = Bundle()
        if (webView?.saveState(state) != null) {
            outState.putBundle(KeyWebViewState, state)
            activeOrigin?.let { outState.putString(KeyWebViewOrigin, it) }
        }
        super.onSaveInstanceState(outState)
    }

    private companion object {
        const val KeyWebViewState = "anilingo.webview.state"
        const val KeyWebViewOrigin = "anilingo.webview.origin"
    }
}

private sealed interface ServerGateState {
    data object Checking : ServerGateState
    data object Compatible : ServerGateState
    data class UpdateRequired(val detail: String) : ServerGateState
    data class Unavailable(val detail: String) : ServerGateState
}

@Composable
private fun AniLingoMobileApp(
    restoredWebState: Bundle?,
    restoredWebOrigin: String?,
    onWebViewChanged: (WebView?) -> Unit,
    onOriginChanged: (String?) -> Unit,
    onFinish: () -> Unit,
) {
    val context = LocalContext.current
    val settings = remember { ServerSettings(context.applicationContext) }
    var originValue by rememberSaveable { mutableStateOf(settings.getOrigin()) }
    var activeEpisodeId by rememberSaveable { mutableStateOf<String?>(null) }
    var securityError by remember { mutableStateOf<String?>(null) }
    var gateRetry by remember { mutableStateOf(0) }
    var currentWebView by remember { mutableStateOf<WebView?>(null) }

    val origin = remember(originValue) {
        originValue?.let { ServerOrigin.parse(it).getOrNull() }
    }

    if (origin == null) {
        onOriginChanged(null)
        ServerSetupScreen(
            initialValue = originValue.orEmpty(),
            onSave = { parsed ->
                settings.setOrigin(parsed)
                originValue = parsed.value
                activeEpisodeId = null
                gateRetry += 1
            },
        )
        return
    }

    onOriginChanged(origin.value)

    val webSession = remember(origin.value) { WebSession(origin) }
    val api = remember(origin.value) {
        HttpAniLingoClientApi(
            origin = origin.value,
            requestHeaders = webSession::requestHeaders,
            responseCookieSink = webSession::acceptResponseCookies,
        )
    }

    var gateState by remember(origin.value, gateRetry) {
        mutableStateOf<ServerGateState>(ServerGateState.Checking)
    }

    LaunchedEffect(origin.value, gateRetry) {
        gateState = ServerGateState.Checking
        gateState = try {
            val capabilities = withContext(Dispatchers.IO) {
                api.getCapabilities()
            }
            when (val compatibility = MobileCompatibilityGate.evaluate(capabilities)) {
                MobileCompatibilityState.Compatible -> ServerGateState.Compatible
                is MobileCompatibilityState.UpdateRequired ->
                    ServerGateState.UpdateRequired(compatibility.detail)
            }
        } catch (exception: Exception) {
            ServerGateState.Unavailable(
                exception.message ?: "The configured AniLingo server could not be reached.",
            )
        }
    }

    when (val gate = gateState) {
        ServerGateState.Checking -> FullscreenStatus(
            title = "Connecting to AniLingo",
            detail = origin.value,
            progress = true,
        )
        is ServerGateState.UpdateRequired -> FullscreenStatus(
            title = "Update required",
            detail = gate.detail,
            progress = false,
            primaryLabel = "Check again",
            onPrimary = { gateRetry += 1 },
            secondaryLabel = "Change server",
            onSecondary = {
                CookieManager.getInstance().removeAllCookies(null)
                CookieManager.getInstance().flush()
                settings.clearOrigin()
                originValue = null
            },
        )
        is ServerGateState.Unavailable -> FullscreenStatus(
            title = "Server unavailable",
            detail = gate.detail,
            progress = false,
            primaryLabel = "Try again",
            onPrimary = { gateRetry += 1 },
            secondaryLabel = "Change server",
            onSecondary = {
                CookieManager.getInstance().removeAllCookies(null)
                CookieManager.getInstance().flush()
                settings.clearOrigin()
                originValue = null
            },
        )
        ServerGateState.Compatible -> {
            Box(modifier = Modifier.fillMaxSize()) {
                AniLingoWebShell(
                    origin = origin,
                    restoredState = restoredWebState.takeIf {
                        restoredWebOrigin == origin.value
                    },
                    modifier = Modifier.fillMaxSize(),
                    onWebViewChanged = {
                        currentWebView = it
                        onWebViewChanged(it)
                    },
                    onEpisodeRequested = { episodeId ->
                        activeEpisodeId = episodeId
                    },
                    onSecurityError = { securityError = it },
                )

                activeEpisodeId?.let { episodeId ->
                    NativePlayerScreen(
                        episodeId = episodeId,
                        origin = origin,
                        api = api,
                        sessionHeaders = webSession::requestHeaders,
                        onClose = {
                            activeEpisodeId = null
                        },
                    )
                }
            }

            BackHandler(enabled = activeEpisodeId == null) {
                when {
                    currentWebView?.canGoBack() == true -> currentWebView?.goBack()
                    else -> onFinish()
                }
            }
        }
    }

    securityError?.let { error ->
        AlertDialog(
            onDismissRequest = { securityError = null },
            title = { Text("Connection blocked") },
            text = { Text(error) },
            confirmButton = {
                TextButton(onClick = { securityError = null }) {
                    Text("OK")
                }
            },
        )
    }
}


@Composable
private fun ServerSetupScreen(
    initialValue: String,
    onSave: (ServerOrigin) -> Unit,
) {
    var value by rememberSaveable(initialValue) { mutableStateOf(initialValue) }
    var error by remember { mutableStateOf<String?>(null) }

    Surface(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier.padding(24.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text(
                text = "Connect AniLingo",
                style = MaterialTheme.typography.headlineMedium,
            )
            Text(
                text = "Enter the origin of your AniLingo server. The normal Library, Learn, Novels and Settings pages stay in the secure app WebView; episode playback opens natively.",
                style = MaterialTheme.typography.bodyMedium,
            )
            OutlinedTextField(
                value = value,
                onValueChange = {
                    value = it
                    error = null
                },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                label = { Text("AniLingo server") },
                placeholder = { Text("https://anilingo.example") },
                isError = error != null,
                supportingText = error?.let { message ->
                    { Text(message) }
                },
            )
            if (value.trim().startsWith("http://", ignoreCase = true)) {
                Text(
                    text = "HTTP is unencrypted. Prefer HTTPS for any server reachable outside a trusted LAN.",
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodySmall,
                )
            }
            Button(
                onClick = {
                    ServerOrigin.parse(value)
                        .onSuccess(onSave)
                        .onFailure {
                            error = it.message ?: "Enter a valid AniLingo server origin."
                        }
                },
            ) {
                Text("Connect")
            }
        }
    }
}

@Composable
private fun FullscreenStatus(
    title: String,
    detail: String,
    progress: Boolean,
    primaryLabel: String? = null,
    onPrimary: (() -> Unit)? = null,
    secondaryLabel: String? = null,
    onSecondary: (() -> Unit)? = null,
) {
    Surface(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            if (progress) {
                CircularProgressIndicator()
            }
            Text(
                text = title,
                style = MaterialTheme.typography.headlineSmall,
                modifier = Modifier.padding(top = 16.dp),
            )
            Text(
                text = detail,
                style = MaterialTheme.typography.bodyMedium,
                modifier = Modifier.padding(top = 8.dp),
            )
            if (primaryLabel != null && onPrimary != null) {
                Row(
                    modifier = Modifier.padding(top = 16.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Button(onClick = onPrimary) {
                        Text(primaryLabel)
                    }
                    if (secondaryLabel != null && onSecondary != null) {
                        TextButton(onClick = onSecondary) {
                            Text(secondaryLabel)
                        }
                    }
                }
            }
        }
    }
}
