package de.juloc.anilingo.core.api

import de.juloc.anilingo.core.model.ClientCapabilities
import de.juloc.anilingo.core.model.PlayerBootstrap
import de.juloc.anilingo.core.model.SubtitleCue
import de.juloc.anilingo.core.model.TermLearningState
import de.juloc.anilingo.core.model.TermSummary

interface AniLingoClientApi {
    suspend fun getCapabilities(): ClientCapabilities

    suspend fun getPlayer(episodeId: String): PlayerBootstrap

    suspend fun getCues(
        episodeId: String,
        trackId: String,
        fromMs: Long? = null,
        toMs: Long? = null,
    ): List<SubtitleCue>

    suspend fun getTerm(termId: String): TermSummary

    suspend fun setTermState(
        termId: String,
        state: TermLearningState,
    ): TermSummary
}

sealed interface ApiCompatibility {
    data object Compatible : ApiCompatibility
    data class ServerTooOld(val serverApiVersion: Int) : ApiCompatibility
    data class ClientTooOld(val minimumClientApiVersion: Int) : ApiCompatibility
}

object ClientApiCompatibility {
    fun evaluate(capabilities: ClientCapabilities): ApiCompatibility =
        when {
            capabilities.minimumClientApiVersion > ClientApiRoutes.ApiVersion ->
                ApiCompatibility.ClientTooOld(capabilities.minimumClientApiVersion)

            capabilities.apiVersion < ClientApiRoutes.ApiVersion ->
                ApiCompatibility.ServerTooOld(capabilities.apiVersion)

            else -> ApiCompatibility.Compatible
        }
}
