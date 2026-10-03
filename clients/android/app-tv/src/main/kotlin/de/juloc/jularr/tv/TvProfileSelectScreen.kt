package de.juloc.jularr.tv

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Person
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.tv.material3.Icon
import androidx.tv.material3.Surface
import androidx.tv.material3.SurfaceDefaults
import androidx.tv.material3.Text

@Composable
fun TvProfileSelectScreen(
    sessions: List<TvSavedSession>,
    onSelectSession: (TvSavedSession) -> Unit,
    onAddAccount: () -> Unit,
) {
    val focusColor = rememberTvFocusColor()
    val cardShape = RoundedCornerShape(16.dp)

    Surface(
        modifier = Modifier.fillMaxSize(),
        colors = SurfaceDefaults.colors(
            containerColor = Color(0xFF0B0D14),
        ),
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(48.dp),
            contentAlignment = Alignment.Center,
        ) {
            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(36.dp),
            ) {
                Text(
                    text = stringResource(R.string.tv_profile_select_title),
                    style = TextStyle(
                        color = Color.White,
                        fontSize = 36.sp,
                        fontWeight = FontWeight.Bold,
                    ),
                )

                Row(
                    horizontalArrangement = Arrangement.spacedBy(28.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    for (session in sessions) {
                        var focused by remember(session.id) { mutableStateOf(false) }

                        Column(
                            horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                            Box(
                                modifier = Modifier
                                    .size(96.dp)
                                    .clip(cardShape)
                                    .background(Color(0xFF5B46F6))
                                    .tvFocusIndication(focused, focusColor, cardShape)
                                    .clickable { onSelectSession(session) }
                                    .reportFocus { focused = it },
                                contentAlignment = Alignment.Center,
                            ) {
                                Icon(
                                    imageVector = Icons.Filled.Person,
                                    contentDescription = session.userName,
                                    tint = Color.White,
                                    modifier = Modifier.size(52.dp),
                                )
                            }

                            Text(
                                text = session.userName,
                                style = TextStyle(
                                    color = Color.White,
                                    fontSize = 18.sp,
                                    fontWeight = FontWeight.Medium,
                                ),
                            )
                        }
                    }

                    // Add Account Card
                    var addFocused by remember { mutableStateOf(false) }
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        Box(
                            modifier = Modifier
                                .size(96.dp)
                                .clip(cardShape)
                                .background(Color(0xFF1E2230))
                                .tvFocusIndication(addFocused, focusColor, cardShape)
                                .clickable(onClick = onAddAccount)
                                .reportFocus { addFocused = it },
                            contentAlignment = Alignment.Center,
                        ) {
                            Icon(
                                imageVector = Icons.Filled.Add,
                                contentDescription = stringResource(R.string.tv_profile_select_add_account),
                                tint = Color.White.copy(alpha = 0.8f),
                                modifier = Modifier.size(48.dp),
                            )
                        }

                        Text(
                            text = stringResource(R.string.tv_profile_select_add_account),
                            style = TextStyle(
                                color = Color.White.copy(alpha = 0.8f),
                                fontSize = 16.sp,
                                fontWeight = FontWeight.Medium,
                            ),
                        )
                    }
                }
            }
        }
    }
}
