package de.juloc.anilingo.tv

import androidx.media3.common.C
import androidx.media3.common.Format
import de.juloc.anilingo.core.model.MediaTrack
import org.junit.Assert.assertTrue
import org.junit.Test

class TvMediaTrackSelectorTest {
    @Test
    fun exactLanguageAndLabelOutscoreCodecOnlyMatch() {
        val requested = MediaTrack(
            id = "stream:2",
            streamIndex = 2,
            kind = "audio",
            codec = "aac",
            language = "jpn",
            title = "Japanese",
            isDefault = true,
            isForced = false,
            isText = false,
        )
        val exact = Format.Builder()
            .setLanguage("jpn")
            .setLabel("Japanese")
            .setSampleMimeType("audio/mp4a-latm")
            .setSelectionFlags(C.SELECTION_FLAG_DEFAULT)
            .build()
        val codecOnly = Format.Builder()
            .setLanguage("eng")
            .setLabel("English")
            .setSampleMimeType("audio/mp4a-latm")
            .build()

        assertTrue(
            TvMediaTrackSelector.score(exact, requested) >
                TvMediaTrackSelector.score(codecOnly, requested),
        )
    }
}
