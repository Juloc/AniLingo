package de.juloc.anilingo.core.api

object ClientApiRoutes {
    const val ApiVersion = 1
    const val Base = "/api/client/v1"
    const val Capabilities = "$Base/capabilities"
    const val Me = "$Base/me"
    const val Library = "$Base/library"

    fun anime(animeId: String) = "$Base/anime/$animeId"
    fun episode(episodeId: String) = "$Base/episodes/$episodeId"
    fun player(episodeId: String) = "$Base/episodes/$episodeId/player"
    fun cues(episodeId: String, trackId: String) =
        "$Base/episodes/$episodeId/cues?trackId=$trackId"
    fun media(mediaFileId: String) = "$Base/media/$mediaFileId/content"
    fun term(termId: String) = "$Base/terms/$termId"
}
