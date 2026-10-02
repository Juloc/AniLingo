package de.juloc.jularr.tv

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

data class TvSavedSession(
    val id: String,
    val serverOrigin: String,
    val userName: String,
    val role: String? = null,
    val profileId: String? = null,
    val cookies: Map<String, String> = emptyMap(),
    val lastActiveAtMillis: Long = System.currentTimeMillis(),
)

class TvSessionStore(context: Context) {
    private val preferences = context.getSharedPreferences(
        PREFS_NAME,
        Context.MODE_PRIVATE,
    )

    @Synchronized
    fun getSessions(): List<TvSavedSession> {
        val rawJson = preferences.getString(KEY_SESSIONS, null) ?: return emptyList()
        return runCatching {
            val array = JSONArray(rawJson)
            val list = mutableListOf<TvSavedSession>()
            for (i in 0 until array.length()) {
                val obj = array.getJSONObject(i)
                val cookieObj = obj.optJSONObject("cookies")
                val cookies = mutableMapOf<String, String>()
                cookieObj?.keys()?.forEach { key ->
                    cookies[key] = cookieObj.getString(key)
                }

                list.add(
                    TvSavedSession(
                        id = obj.getString("id"),
                        serverOrigin = obj.getString("serverOrigin"),
                        userName = obj.getString("userName"),
                        role = obj.optString("role").takeIf { it.isNotBlank() },
                        profileId = obj.optString("profileId").takeIf { it.isNotBlank() },
                        cookies = cookies,
                        lastActiveAtMillis = obj.optLong("lastActiveAtMillis", System.currentTimeMillis()),
                    ),
                )
            }
            list.sortedByDescending { it.lastActiveAtMillis }
        }.getOrDefault(emptyList())
    }

    @Synchronized
    fun getActiveSession(): TvSavedSession? {
        val activeId = preferences.getString(KEY_ACTIVE_ID, null) ?: return getSessions().firstOrNull()
        return getSessions().firstOrNull { it.id == activeId } ?: getSessions().firstOrNull()
    }

    @Synchronized
    fun setActiveSessionId(id: String) {
        preferences.edit().putString(KEY_ACTIVE_ID, id).apply()
        val sessions = getSessions().map {
            if (it.id == id) it.copy(lastActiveAtMillis = System.currentTimeMillis()) else it
        }
        persistSessions(sessions)
    }

    @Synchronized
    fun saveSession(session: TvSavedSession) {
        val current = getSessions().toMutableList()
        current.removeAll { it.id == session.id }
        current.add(0, session.copy(lastActiveAtMillis = System.currentTimeMillis()))
        persistSessions(current)
        preferences.edit().putString(KEY_ACTIVE_ID, session.id).apply()
    }

    @Synchronized
    fun updateCookiesForActiveSession(cookies: Map<String, String>) {
        val active = getActiveSession() ?: return
        val updated = active.copy(
            cookies = cookies,
            lastActiveAtMillis = System.currentTimeMillis(),
        )
        val current = getSessions().toMutableList()
        current.removeAll { it.id == active.id }
        current.add(0, updated)
        persistSessions(current)
    }

    @Synchronized
    fun removeSession(id: String) {
        val current = getSessions().toMutableList()
        current.removeAll { it.id == id }
        persistSessions(current)
        if (preferences.getString(KEY_ACTIVE_ID, null) == id) {
            val next = current.firstOrNull()?.id
            preferences.edit().putString(KEY_ACTIVE_ID, next).apply()
        }
    }

    @Synchronized
    fun clearAll() {
        preferences.edit().clear().apply()
    }

    private fun persistSessions(sessions: List<TvSavedSession>) {
        val array = JSONArray()
        for (session in sessions) {
            val obj = JSONObject()
            obj.put("id", session.id)
            obj.put("serverOrigin", session.serverOrigin)
            obj.put("userName", session.userName)
            obj.put("role", session.role ?: "")
            obj.put("profileId", session.profileId ?: "")
            obj.put("lastActiveAtMillis", session.lastActiveAtMillis)

            val cookieObj = JSONObject()
            session.cookies.forEach { (k, v) ->
                cookieObj.put(k, v)
            }
            obj.put("cookies", cookieObj)

            array.put(obj)
        }
        preferences.edit().putString(KEY_SESSIONS, array.toString()).apply()
    }

    private companion object {
        const val PREFS_NAME = "jularr-tv-sessions"
        const val KEY_SESSIONS = "saved_sessions"
        const val KEY_ACTIVE_ID = "active_session_id"
    }
}
