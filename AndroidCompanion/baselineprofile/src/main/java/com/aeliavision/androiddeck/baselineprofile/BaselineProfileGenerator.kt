package com.aeliavision.androiddeck.baselineprofile

import androidx.benchmark.macro.junit4.BaselineProfileRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Generates Baseline Profiles for the app to optimize startup and critical journeys.
 */
@RunWith(AndroidJUnit4::class)
class BaselineProfileGenerator {

    @get:Rule
    val baselineProfileRule = BaselineProfileRule()

    @Test
    fun generate() = baselineProfileRule.collect(
        packageName = "com.aeliavision.androiddeck",
        includeInvolvedDestinations = true
    ) {
        pressHome()
        startActivityAndWait()
        device.waitForIdle()
    }
}
