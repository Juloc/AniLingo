package de.juloc.anilingo.tv

enum class TvProgressEvent {
    HEARTBEAT,
    PAUSE,
    SEEK,
    BACKGROUND,
    CLOSE,
}

data class TvProgressWrite(
    val positionMs: Long,
    val durationMs: Long?,
    val completed: Boolean,
)

class TvProgressPolicy(
    private val heartbeatIntervalMs: Long = 5_000,
    private val completionThreshold: Double = 0.95,
) {
    private var lastPersistedAtMs: Long? = null

    init {
        require(heartbeatIntervalMs > 0)
        require(completionThreshold in 0.5..1.0)
    }

    fun evaluate(
        event: TvProgressEvent,
        nowMs: Long,
        positionMs: Long,
        durationMs: Long?,
    ): TvProgressWrite? {
        val normalizedPosition = positionMs.coerceAtLeast(0)
        val normalizedDuration = durationMs?.takeIf { it > 0 }
        val immediate = event != TvProgressEvent.HEARTBEAT
        val due = lastPersistedAtMs?.let {
            nowMs - it >= heartbeatIntervalMs
        } ?: true

        if (!immediate && !due) {
            return null
        }

        lastPersistedAtMs = nowMs
        val completed = normalizedDuration?.let {
            normalizedPosition.toDouble() / it.toDouble() >= completionThreshold
        } ?: false

        return TvProgressWrite(
            positionMs = normalizedPosition,
            durationMs = normalizedDuration,
            completed = completed,
        )
    }
}
