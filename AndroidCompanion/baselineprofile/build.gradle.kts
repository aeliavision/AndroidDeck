plugins {
    alias(libs.plugins.android.test)
    alias(libs.plugins.baselineprofile)
}

android {
    namespace = "com.aeliavision.androiddeck.baselineprofile"
    compileSdk = 37

    defaultConfig {
        minSdk = 29
        targetSdk = 37

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    targetProjectPath = ":app"
}

dependencies {
    implementation(libs.androidx.test.ext.junit)
    implementation(libs.androidx.benchmark.macro.junit4)
    implementation(libs.androidx.uiautomator)
}

baselineProfile {
    useConnectedDevices = true
}
