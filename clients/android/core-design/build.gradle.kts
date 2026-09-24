plugins {
    alias(libs.plugins.android.library)
    alias(libs.plugins.kotlin.android)
}

android {
    namespace = "de.juloc.anilingo.core.design"
    compileSdk = 36

    defaultConfig {
        minSdk = 26
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    jvmToolchain(17)
}

val generatedAssetsDir = layout.buildDirectory.dir("generated/playerDesign/assets")

tasks.register<Sync>("syncPlayerDesignAssets") {
    from(rootProject.file("../../design/player")) {
        include("player-tokens.json", "player-icons.json", "player-actions.json")
    }
    into(generatedAssetsDir)
}

android.sourceSets["main"].assets.srcDir(generatedAssetsDir)

tasks.named("preBuild").configure {
    dependsOn("syncPlayerDesignAssets")
}
