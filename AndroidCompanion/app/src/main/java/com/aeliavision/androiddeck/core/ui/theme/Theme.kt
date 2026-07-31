package com.aeliavision.androiddeck.core.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable

/**
 * AndroidDeck's semantic Material 3 theme. Colors, shapes, spacing, and type
 * metrics originate in the repository-level design-tokens.json file.
 */
@Composable
fun AndroidDeckTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = if (darkTheme) AppDarkColorScheme else AppLightColorScheme,
        typography = AppTypography,
        shapes = AppShapes,
        content = content
    )
}
