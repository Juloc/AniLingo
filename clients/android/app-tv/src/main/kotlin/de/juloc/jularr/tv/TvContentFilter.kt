package de.juloc.jularr.tv

enum class TvContentFilter(val labelRes: Int) {
    ALL(R.string.tv_home_filter_all),
    ANIME(R.string.tv_home_filter_anime),
    SERIES(R.string.tv_home_filter_series),
    MOVIES(R.string.tv_home_filter_movies),
    ;

    companion object {
        val visible: List<TvContentFilter> = listOf(
            ALL,
            ANIME,
            SERIES,
            MOVIES,
        )
    }
}
