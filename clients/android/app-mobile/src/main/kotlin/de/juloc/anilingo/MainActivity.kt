package de.juloc.anilingo

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme {
                AniLingoMobileRoot()
            }
        }
    }
}

@Composable
private fun AniLingoMobileRoot() {
    Surface(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier.padding(24.dp),
            horizontalAlignment = Alignment.Start,
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(
                text = "AniLingo",
                style = MaterialTheme.typography.headlineMedium,
            )
            Text(
                text = "Android client foundation",
                style = MaterialTheme.typography.bodyLarge,
            )
            Text(
                text = "The next slice connects the same-origin web shell and native episode player to /api/client/v1.",
                style = MaterialTheme.typography.bodyMedium,
            )
        }
    }
}
