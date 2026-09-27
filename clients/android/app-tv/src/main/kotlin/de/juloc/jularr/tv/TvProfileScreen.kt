package de.juloc.jularr.tv

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Button
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Surface
import androidx.tv.material3.Text
import de.juloc.jularr.core.model.ClientAccount

/**
 * The TV sidebar's Profile/Settings destination (#522): account identity and the two
 * account-level actions the TV app exposes today (sign out, change server). There is no
 * dedicated Settings screen yet, so those live here rather than nowhere.
 */
@Composable
fun TvProfileScreen(
    account: ClientAccount,
    serverOrigin: String,
    onSignOut: () -> Unit,
    onChangeServer: () -> Unit,
) {
    Surface(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(48.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp),
        ) {
            Text(
                stringResource(R.string.tv_profile_title),
                style = MaterialTheme.typography.headlineLarge,
            )
            Text(
                text = account.userName ?: stringResource(
                    if (account.role == "owner") {
                        R.string.tv_profile_role_owner
                    } else {
                        R.string.tv_profile_role_user
                    },
                ),
                style = MaterialTheme.typography.titleLarge,
            )
            Text(
                text = "${stringResource(R.string.tv_profile_server_label)}: $serverOrigin",
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.72f),
            )

            Row(horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                Button(onClick = onSignOut) {
                    Text(stringResource(R.string.tv_profile_sign_out))
                }
                Button(onClick = onChangeServer) {
                    Text(stringResource(R.string.tv_profile_change_server))
                }
            }
        }
    }
}
