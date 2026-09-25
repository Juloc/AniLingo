package de.juloc.anilingo.tv

sealed interface TvRoute {
    data object Setup : TvRoute
    data object Login : TvRoute
    data object Library : TvRoute
    data class Anime(val animeId: String) : TvRoute
    data class Player(
        val episodeId: String,
        val animeId: String,
    ) : TvRoute
}

data class TvNavigationState(
    val route: TvRoute,
    val previous: List<TvRoute> = emptyList(),
)

object TvNavigation {
    fun initial(hasServerOrigin: Boolean): TvNavigationState =
        TvNavigationState(
            route = if (hasServerOrigin) TvRoute.Login else TvRoute.Setup,
        )

    fun connected(state: TvNavigationState): TvNavigationState =
        state.replace(TvRoute.Login)

    fun signedIn(state: TvNavigationState): TvNavigationState =
        state.replace(TvRoute.Library)

    fun openAnime(
        state: TvNavigationState,
        animeId: String,
    ): TvNavigationState =
        state.push(TvRoute.Anime(animeId))

    fun openPlayer(
        state: TvNavigationState,
        episodeId: String,
        animeId: String,
    ): TvNavigationState =
        state.push(TvRoute.Player(episodeId, animeId))

    fun signOut(state: TvNavigationState): TvNavigationState =
        TvNavigationState(TvRoute.Login)

    fun changeServer(): TvNavigationState =
        TvNavigationState(TvRoute.Setup)

    fun back(state: TvNavigationState): TvNavigationState? {
        if (state.previous.isEmpty()) {
            return when (state.route) {
                TvRoute.Setup,
                TvRoute.Login,
                TvRoute.Library,
                -> null

                is TvRoute.Anime -> TvNavigationState(TvRoute.Library)
                is TvRoute.Player -> TvNavigationState(
                    TvRoute.Anime(state.route.animeId),
                )
            }
        }

        return TvNavigationState(
            route = state.previous.last(),
            previous = state.previous.dropLast(1),
        )
    }

    private fun TvNavigationState.push(route: TvRoute): TvNavigationState =
        TvNavigationState(
            route = route,
            previous = previous + this.route,
        )

    private fun TvNavigationState.replace(route: TvRoute): TvNavigationState =
        copy(route = route)
}
