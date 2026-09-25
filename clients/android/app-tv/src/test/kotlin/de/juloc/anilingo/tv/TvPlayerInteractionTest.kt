package de.juloc.anilingo.tv

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class TvPlayerInteractionTest {
    @Test
    fun hiddenControlsUseLeftRightForTenSecondSeeking() {
        val left = TvPlayerInteraction.left(TvPlayerUiState(), wordCount = 0)
        val right = TvPlayerInteraction.right(TvPlayerUiState(), wordCount = 0)

        assertEquals(listOf(TvPlayerEffect.SeekBy(-10_000)), left.effects)
        assertEquals(listOf(TvPlayerEffect.SeekBy(10_000)), right.effects)
    }

    @Test
    fun okShowsControlsAndThenLetsFocusedControlHandleActivation() {
        val shown = TvPlayerInteraction.ok(TvPlayerUiState(), wordCount = 0)
        val visible = TvPlayerInteraction.ok(shown.state, wordCount = 0)

        assertTrue(shown.state.controlsVisible)
        assertTrue(visible.state.controlsVisible)
        assertEquals(emptyList<TvPlayerEffect>(), visible.effects)
    }

    @Test
    fun enteringLearningPausesOnlyWhenPlaybackWasRunning() {
        val playing = TvPlayerInteraction.learnCurrentLine(
            TvPlayerUiState(controlsVisible = true),
            isPlaying = true,
            wordCount = 3,
        )
        val paused = TvPlayerInteraction.learnCurrentLine(
            TvPlayerUiState(controlsVisible = true),
            isPlaying = false,
            wordCount = 3,
        )

        assertEquals(TvLearningLayer.SENTENCE, playing.state.learningLayer)
        assertEquals(listOf(TvPlayerEffect.PausePlayback), playing.effects)
        assertEquals(emptyList<TvPlayerEffect>(), paused.effects)
    }

    @Test
    fun leavingLearningResumesOnlyWhenItEnteredFromPlayingState() {
        val fromPlaying = TvPlayerInteraction.back(
            TvPlayerUiState(
                learningLayer = TvLearningLayer.SENTENCE,
                resumeAfterLearning = true,
            ),
        )
        val fromPaused = TvPlayerInteraction.back(
            TvPlayerUiState(
                learningLayer = TvLearningLayer.SENTENCE,
                resumeAfterLearning = false,
            ),
        )

        assertEquals(listOf(TvPlayerEffect.ResumePlayback), fromPlaying.effects)
        assertEquals(emptyList<TvPlayerEffect>(), fromPaused.effects)
    }

    @Test
    fun wordFocusIsBoundedAndBackClosesOneLearningLevelAtATime() {
        var state = TvPlayerUiState(
            learningLayer = TvLearningLayer.SENTENCE,
            focusedWordIndex = 0,
        )
        state = TvPlayerInteraction.left(state, wordCount = 2).state
        assertEquals(0, state.focusedWordIndex)

        state = TvPlayerInteraction.right(state, wordCount = 2).state
        state = TvPlayerInteraction.right(state, wordCount = 2).state
        assertEquals(1, state.focusedWordIndex)

        state = TvPlayerInteraction.ok(state, wordCount = 2).state
        assertEquals(TvLearningLayer.WORD, state.learningLayer)

        state = TvPlayerInteraction.back(state).state
        assertEquals(TvLearningLayer.SENTENCE, state.learningLayer)
    }

    @Test
    fun backHidesControlsBeforeLeavingPlayer() {
        val hide = TvPlayerInteraction.back(TvPlayerUiState(controlsVisible = true))
        val exit = TvPlayerInteraction.back(hide.state)

        assertEquals(false, hide.state.controlsVisible)
        assertEquals(listOf(TvPlayerEffect.ExitPlayer), exit.effects)
    }
}
