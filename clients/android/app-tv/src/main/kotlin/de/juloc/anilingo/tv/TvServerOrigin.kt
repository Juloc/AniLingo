package de.juloc.anilingo.tv

import java.net.URI

object TvServerOrigin {
    fun normalize(input: String): String {
        val trimmed = input.trim()
        require(trimmed.isNotEmpty()) { "Enter the AniLingo server address." }

        val uri = URI(trimmed)
        require(uri.scheme.equals("http", true) || uri.scheme.equals("https", true)) {
            "The AniLingo server address must start with http:// or https://."
        }
        require(!uri.host.isNullOrBlank()) {
            "The AniLingo server address must include a host."
        }
        require(uri.userInfo == null && uri.query == null && uri.fragment == null) {
            "The AniLingo server address cannot contain credentials, query parameters or a fragment."
        }
        require(uri.path.isNullOrBlank() || uri.path == "/") {
            "Enter only the AniLingo server origin, without an application path."
        }

        return URI(
            uri.scheme.lowercase(),
            null,
            uri.host.lowercase(),
            uri.port,
            null,
            null,
            null,
        ).toString()
    }
}
