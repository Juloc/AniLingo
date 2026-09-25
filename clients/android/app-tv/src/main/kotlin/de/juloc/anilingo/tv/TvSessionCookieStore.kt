package de.juloc.anilingo.tv

class TvSessionCookieStore {
    private val cookies = linkedMapOf<String, String>()

    @Synchronized
    fun accept(setCookieHeaders: List<String>) {
        for (header in setCookieHeaders) {
            val pair = header.substringBefore(';').trim()
            val separator = pair.indexOf('=')
            if (separator <= 0) continue

            val name = pair.substring(0, separator).trim()
            val value = pair.substring(separator + 1).trim()
            if (name.isEmpty()) continue

            val expires = header.lowercase()
            val removesCookie = value.isEmpty() ||
                "max-age=0" in expires ||
                "max-age=-1" in expires

            if (removesCookie) {
                cookies.remove(name)
            } else {
                cookies[name] = value
            }
        }
    }

    @Synchronized
    fun requestHeaders(): Map<String, String> =
        if (cookies.isEmpty()) {
            emptyMap()
        } else {
            mapOf(
                "Cookie" to cookies.entries.joinToString("; ") { (name, value) ->
                    "$name=$value"
                },
            )
        }

    @Synchronized
    fun clear() {
        cookies.clear()
    }

    @Synchronized
    fun isEmpty(): Boolean = cookies.isEmpty()
}
