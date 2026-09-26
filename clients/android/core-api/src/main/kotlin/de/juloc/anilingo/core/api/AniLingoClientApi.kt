package de.juloc.anilingo.core.api

import de.juloc.anilingo.core.model.AnimeDetail
import de.juloc.anilingo.core.model.ClientAccount
import de.juloc.anilingo.core.model.ClientCapabilities
import de.juloc.anilingo.core.model.ClientLibrary
import de.juloc.anilingo.core.model.ClientLogin
import de.juloc.anilingo.core.model.CueResponse
import de.juloc.anilingo.core.model.EpisodeDetail
import de.juloc.anilingo.core.model.EpisodeProgress
import de.juloc.anilingo.core.model.EpisodeProgressUpdate
import de.juloc.anilingo.core.model.MediaAvailability
import de.juloc.anilingo.core.model.PlayerBootstrap
import de.juloc.anilingo.core.model.RootAvailability
import de.juloc.anilingo.core.model.SpeechModelsResponse
import de.juloc.anilingo.core.model.TermDetail
import de.juloc.anilingo.core.model.TermStateResult
import de.juloc.anilingo.core.model.TtsPreferences
import de.juloc.anilingo.core.model.TtsPreferencesUpdate

interface AniLingoClientApi {
    suspend fun getCapabilities(): ClientCapabilities
    suspend fun login(credentials: ClientLogin): ClientAccount
    suspend fun logout()
    suspend fun getMe(): ClientAccount
    suspend fun getLibrary(): ClientLibrary
    suspend fun getAnime(animeId: String): AnimeDetail
    suspend fun getEpisode(episodeId: String): EpisodeDetail
    suspend fun getProgress(episodeId: String): EpisodeProgress
    suspend fun setProgress(
        episodeId: String,
        update: EpisodeProgressUpdate,
    ): EpisodeProgress

    suspend fun getPlayer(episodeId: String): PlayerBootstrap

    suspend fun getCues(
        episodeId: String,
        trackId: String? = null,
        fromMs: Int? = null,
        toMs: Int? = null,
    ): CueResponse

    suspend fun getMediaAvailability(
        mediaFileId: String,
        fresh: Boolean = false,
    ): MediaAvailability

    suspend fun getTerm(termId: String): TermDetail
    suspend fun setTermState(
        termId: String,
        state: String,
    ): TermStateResult

    suspend fun getRootAvailability(rootId: String): RootAvailability
    suspend fun testRoot(rootId: String): RootAvailability
    suspend fun wakeRoot(rootId: String): RootAvailability

    suspend fun getTtsPreferences(): TtsPreferences
    suspend fun updateTtsPreferences(update: TtsPreferencesUpdate): TtsPreferences
    suspend fun getSpeechModels(): SpeechModelsResponse
}

sealed interface ApiCompatibility {
    data object Compatible : ApiCompatibility
    data class ServerTooOld(val serverApiVersion: Int) : ApiCompatibility
    data class ClientTooOld(val minimumSupportedApiVersion: Int) : ApiCompatibility
}

object ClientApiCompatibility {
    fun evaluate(capabilities: ClientCapabilities): ApiCompatibility =
        when {
            capabilities.minimumSupportedApiVersion > ClientApiRoutes.ApiVersion ->
                ApiCompatibility.ClientTooOld(capabilities.minimumSupportedApiVersion)

            capabilities.apiVersion < ClientApiRoutes.ApiVersion ->
                ApiCompatibility.ServerTooOld(capabilities.apiVersion)

            else -> ApiCompatibility.Compatible
        }
}
