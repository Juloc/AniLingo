package de.juloc.jularr.tv

/**
 * Home and Search's content-type filter (#522: "All / Movies / TV / Anime"). Only the
 * filters the server API actually has data for are shown: Jularr's client API models a
 * single `Anime` media type end to end (docs/INFORMATION_ARCHITECTURE.md §5.1, "Movies and
 * TV library — Missing — Jularr has no Movie or TV Show entity, acquisition, or naming
 * distinct from anime"; tracked by #396). Movies and TV are therefore omitted here rather
 * than shown as dead filters with no matching content; the PR notes this gap so it is easy
 * to add [MOVIES]/[TV] once #396 gives them a real data source.
 */
enum class TvContentFilter(val labelRes: Int) {
    ALL(R.string.tv_home_filter_all),
    ANIME(R.string.tv_home_filter_anime),
    ;

    companion object {
        /** All filters currently backed by data; nothing is hidden dynamically today. */
        val visible: List<TvContentFilter> = listOf(ALL, ANIME)
    }
}
