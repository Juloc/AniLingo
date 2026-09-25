package de.juloc.anilingo.tv

import de.juloc.anilingo.core.api.AniLingoClientApi
import de.juloc.anilingo.core.api.ApiCompatibility
import de.juloc.anilingo.core.api.ClientApiCompatibility
import de.juloc.anilingo.core.model.AnimeDetail
import de.juloc.anilingo.core.model.ClientAccount
import de.juloc.anilingo.core.model.ClientCapabilities
import de.juloc.anilingo.core.model.ClientLibrary
import de.juloc.anilingo.core.model.ClientLogin
import de.juloc.anilingo.core.model.CueResponse
import de.juloc.anilingo.core.model.EpisodeProgress
import de.juloc.anilingo.core.model.EpisodeProgressUpdate
import de.juloc.anilingo.core.model.MediaAvailability
import de.juloc.anilingo.core.model.PlayerBootstrap
import de.juloc.anilingo.core.model.RootAvailability
import de.juloc.anilingo.core.model.TermStateResult

class TvClientFlow(
    private val apiFactory: (String) -> AniLingoClientApi,
) {
    private var api: AniLingoClientApi? = null

    var origin: String? = null
        private set

    suspend fun connect(rawOrigin: String): ClientCapabilities {
        val normalized = TvServerOrigin.normalize(rawOrigin)
        val client = apiFactory(normalized)
        val capabilities = client.getCapabilities()

        when (val compatibility = ClientApiCompatibility.evaluate(capabilities)) {
            ApiCompatibility.Compatible -> Unit
            is ApiCompatibility.ClientTooOld -> throw TvClientCompatibilityException(
                "This AniLingo server requires client API ${compatibility.minimumSupportedApiVersion}. Update the TV app.",
            )
            is ApiCompatibility.ServerTooOld -> throw TvClientCompatibilityException(
                "This TV app requires client API 1, but the server provides ${compatibility.serverApiVersion}. Update AniLingo.",
            )
        }

        if (!capabilities.features.nativeSessionAuth) {
            throw TvClientCompatibilityException(
                "This AniLingo server does not support native TV sign-in.",
            )
        }

        origin = normalized
        api = client
        return capabilities
    }

    suspend fun login(
        userName: String,
        password: String,
    ): TvSignedInData {
        val client = requireApi()
        val account = client.login(
            ClientLogin(
                userName = userName,
                password = password,
                rememberMe = true,
            ),
        )
        return TvSignedInData(
            account = account,
            library = client.getLibrary(),
        )
    }

    suspend fun refreshLibrary(): ClientLibrary =
        requireApi().getLibrary()

    suspend fun loadAnime(animeId: String): AnimeDetail =
        requireApi().getAnime(animeId)

    suspend fun loadEpisode(episodeId: String): TvEpisodeBundle {
        val client = requireApi()
        val bootstrap = client.getPlayer(episodeId)
        val progress = client.getProgress(episodeId)
        val activeTrackId = bootstrap.activeLearningSubtitleTrackId
        val cues = if (activeTrackId == null) {
            emptyCueWindow()
        } else {
            loadCueWindow(
                episodeId = episodeId,
                trackId = activeTrackId,
                positionMs = progress.positionMs,
            )
        }

        return TvEpisodeBundle(
            bootstrap = bootstrap,
            progress = progress,
            cues = cues,
        )
    }

    suspend fun loadCueWindow(
        episodeId: String,
        trackId: String,
        positionMs: Long,
        beforeMs: Int = 5_000,
        afterMs: Int = 60_000,
    ): CueResponse {
        require(beforeMs >= 0 && afterMs > 0) {
            "Cue window bounds must be positive."
        }

        val center = positionMs.coerceAtLeast(0)
        val from = (center - beforeMs)
            .coerceAtLeast(0)
            .coerceAtMost(Int.MAX_VALUE.toLong())
            .toInt()
        val to = (center + afterMs)
            .coerceAtMost(Int.MAX_VALUE.toLong())
            .toInt()

        return requireApi().getCues(
            episodeId = episodeId,
            trackId = trackId,
            fromMs = from,
            toMs = to,
        )
    }

    suspend fun refreshMediaAvailability(
        mediaFileId: String,
        fresh: Boolean = true,
    ): MediaAvailability =
        requireApi().getMediaAvailability(mediaFileId, fresh)

    suspend fun wakeRoot(rootId: String): RootAvailability =
        requireApi().wakeRoot(rootId)

    suspend fun saveProgress(
        episodeId: String,
        positionMs: Long,
        durationMs: Long?,
        completed: Boolean,
    ): EpisodeProgress =
        requireApi().setProgress(
            episodeId,
            EpisodeProgressUpdate(
                positionMs = positionMs.coerceAtLeast(0),
                durationMs = durationMs?.coerceAtLeast(0),
                completed = completed,
            ),
        )

    suspend fun setTermState(
        termId: String,
        state: String,
    ): TermStateResult {
        require(state == "known" || state == "learning") {
            "TV learning state must be known or learning."
        }
        return requireApi().setTermState(termId, state)
    }

    suspend fun logout() {
        requireApi().logout()
    }

    private fun requireApi(): AniLingoClientApi =
        api ?: error("AniLingo TV has not connected to a server yet.")

    private fun emptyCueWindow() = CueResponse(
        trackId = null,
        fromMs = null,
        toMs = null,
        cues = emptyList(),
    )
}

data class TvSignedInData(
    val account: ClientAccount,
    val library: ClientLibrary,
)

data class TvEpisodeBundle(
    val bootstrap: PlayerBootstrap,
    val progress: EpisodeProgress,
    val cues: CueResponse,
)

class TvClientCompatibilityException(
    message: String,
) : IllegalStateException(message)
