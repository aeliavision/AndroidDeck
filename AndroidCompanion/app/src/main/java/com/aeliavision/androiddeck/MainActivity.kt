package com.aeliavision.androiddeck

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.getValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.compose.material3.windowsizeclass.ExperimentalMaterial3WindowSizeClassApi
import androidx.compose.material3.windowsizeclass.calculateWindowSizeClass
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import com.aeliavision.androiddeck.core.navigation.AppNavHost
import com.aeliavision.androiddeck.core.ui.theme.AndroidDeckTheme
import com.aeliavision.androiddeck.data.AuthPreferencesStore
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

/**
 * Single-activity entry point.
 * All UI is handled by Jetpack Compose via AppNavHost.
 */
@AndroidEntryPoint
class MainActivity : ComponentActivity() {

    @Inject lateinit var preferencesStore: AuthPreferencesStore

    @OptIn(ExperimentalMaterial3WindowSizeClassApi::class)
    override fun onCreate(savedInstanceState: Bundle?) {
        installSplashScreen()
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        setContent {
            // Observe theme setting with lifecycle awareness
            val darkMode by preferencesStore.darkMode.collectAsStateWithLifecycle(initialValue = "system")
            val useDarkTheme = when(darkMode) {
                "light" -> false
                "dark" -> true
                else -> isSystemInDarkTheme()
            }
            val windowSizeClass = calculateWindowSizeClass(this)
            
            AndroidDeckTheme(darkTheme = useDarkTheme) {
                AppNavHost(windowSizeClass = windowSizeClass)
            }
        }
    }
}
