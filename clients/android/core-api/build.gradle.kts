plugins {
    alias(libs.plugins.android.library)
}

android {
    namespace = "de.juloc.anilingo.core.api"
    compileSdk = 37

    defaultConfig {
        minSdk = 26
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}


dependencies {
    api(project(":core-model"))
    testImplementation(libs.junit)
}
