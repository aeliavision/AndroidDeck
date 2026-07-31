// Top-level build file where you can add configuration options common to all sub-projects/modules.
// STD-AND04: Kotlin 2.4.0 (2026 stable).
// STD-AND01: KSP replaces kapt for Hilt annotation processing.
plugins {
    alias(libs.plugins.android.application) apply false
    alias(libs.plugins.android.test) apply false
    alias(libs.plugins.kotlin.compose) apply false
    alias(libs.plugins.kotlin.serialization) apply false
    alias(libs.plugins.hilt) apply false
    alias(libs.plugins.ksp) apply false
}
