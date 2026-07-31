plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.hilt)
    alias(libs.plugins.ksp)
}

kotlin {
    jvmToolchain(17)
}

android {
    namespace = "com.aeliavision.androiddeck"
    compileSdk = 37

    defaultConfig {
        applicationId = "com.aeliavision.androiddeck"
        minSdk = 29
        targetSdk = 37
        versionCode = 3
        versionName = "3.0.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        vectorDrawables.useSupportLibrary = true
    }

    lint {
        abortOnError = true
        checkReleaseBuilds = true
        warningsAsErrors = true
        baseline = file("lint-baseline.xml")
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }

    compileOptions {
        isCoreLibraryDesugaringEnabled = true
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    packaging {
        resources {
            excludes += "META-INF/INDEX.LIST"
            excludes += "META-INF/io.netty.versions.properties"
            excludes += "META-INF/DEPENDENCIES"
            excludes += "META-INF/{AL2.0,LGPL2.1}"
            excludes += "META-INF/LICENSE.md"
            excludes += "META-INF/NOTICE.md"
        }
    }
}

configurations.all {
    exclude(group = "io.netty", module = "netty-transport-native-kqueue")
}

dependencies {
    coreLibraryDesugaring(libs.desugarJdkLibs)

    // Compose BOM
    implementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(platform(libs.androidx.compose.bom))

    // Compose core
    implementation(libs.androidx.ui)
    implementation(libs.androidx.ui.tooling.preview)
    implementation(libs.androidx.ui.graphics)
    debugImplementation(libs.androidx.ui.tooling)
    debugImplementation(libs.androidx.ui.test.manifest)

    // Material3
    implementation(libs.androidx.material3)
    implementation(libs.androidx.material3.adaptive.navigation.suite)
    implementation(libs.androidx.material.icons.extended)
    implementation(libs.androidx.material3.windowsizeclass)

    // Activity + Compose integration
    implementation(libs.androidx.activity.compose)

    // Navigation 3 (2026 standard)
    implementation(libs.androidx.navigation3.runtime)
    implementation(libs.androidx.navigation3.ui)
    implementation(libs.kotlinx.serialization.json)

    // Lifecycle / ViewModel
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)

    // Coroutines
    implementation(libs.kotlinx.coroutines.android)

    // Hilt DI
    implementation(libs.hilt.android)
    ksp(libs.hilt.compiler)
    implementation(libs.hilt.navigation.compose)

    // Image loading
    implementation(libs.coil.compose)
    implementation(libs.coil.network.okhttp)

    // AndroidX core
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.core.splashscreen)

    // AppCompat
    implementation(libs.androidx.appcompat)

    // DataStore
    implementation(libs.androidx.datastore.preferences)

    // WorkManager
    implementation(libs.androidx.work.runtime.ktx)
    implementation(libs.hilt.work)
    ksp(libs.hilt.ext.compiler)

    // Ktor Netty
    implementation(libs.ktor.server.netty)
    implementation(libs.netty.handler)
    implementation(libs.netty.transport.classes.kqueue)
    implementation(libs.ktor.server.core)
    implementation(libs.ktor.server.content.negotiation)
    implementation(libs.ktor.serialization.gson)
    implementation(libs.ktor.server.status.pages)
    implementation(libs.ktor.server.compression)
    implementation(libs.ktor.server.double.receive)
    implementation(libs.ktor.io)

    // JSON serialization
    implementation(libs.gson)

    // Baseline Profiles
    implementation(libs.androidx.profileinstaller)

    // Testing
    testImplementation(libs.junit)
    testImplementation(libs.mockito.core)
    testImplementation(libs.mockito.kotlin)
    testImplementation(libs.kotlinx.coroutines.test)
    androidTestImplementation(libs.androidx.test.ext.junit)
    androidTestImplementation(libs.androidx.ui.test.junit4)
}
