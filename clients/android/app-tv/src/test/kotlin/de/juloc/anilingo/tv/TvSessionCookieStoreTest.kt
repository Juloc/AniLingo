package de.juloc.anilingo.tv

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class TvSessionCookieStoreTest {
    @Test
    fun capturesOnlyCookiePairsForRequests() {
        val store = TvSessionCookieStore()
        store.accept(
            listOf(
                ".AspNetCore.Cookies=abc.def==; path=/; httponly; samesite=lax",
                "other=value; path=/",
            ),
        )

        assertEquals(
            mapOf("Cookie" to ".AspNetCore.Cookies=abc.def==; other=value"),
            store.requestHeaders(),
        )
    }

    @Test
    fun expiredCookieIsRemoved() {
        val store = TvSessionCookieStore()
        store.accept(listOf(".AspNetCore.Cookies=session; path=/"))
        store.accept(listOf(".AspNetCore.Cookies=; expires=Thu, 01 Jan 1970 00:00:00 GMT; max-age=0"))

        assertTrue(store.requestHeaders().isEmpty())
        assertTrue(store.isEmpty())
    }
}
