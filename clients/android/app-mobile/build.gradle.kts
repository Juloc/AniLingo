plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.compose.compiler)
}

android {
    namespace = "de.juloc.anilingo"
    compileSdk = 36

    defaultConfig {
        applicationId = "de.juloc.anilingo"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0-dev"
    }

    buildFeatures {
        compose = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    implementation(project(":core-api"))
    implementation(project(":core-model"))
    implementation(project(":core-player"))
    implementation(project(":core-design"))

    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.foundation)
    implementation(libs.androidx.material3)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation("androidx.media3:media3-ui:1.11.1")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    implementation(libs.androidx.work.runtime)

    testImplementation(libs.junit)
    // Real org.json for JVM unit tests of the offline state codec (android.jar only ships stubs).
    testImplementation(libs.org.json)
    debugImplementation(libs.androidx.compose.ui.tooling)
}


tasks.matching { it.name == "lintDebug" }.configureEach {
    dependsOn("testDebugUnitTest")
}

