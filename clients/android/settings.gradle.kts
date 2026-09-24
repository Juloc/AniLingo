pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "AniLingoAndroid"

include(
    ":app-mobile",
    ":app-tv",
    ":core-api",
    ":core-model",
    ":core-player",
    ":core-session",
    ":core-design",
)
