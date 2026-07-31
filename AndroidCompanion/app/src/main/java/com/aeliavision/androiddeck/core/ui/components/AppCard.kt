package com.aeliavision.androiddeck.core.ui.components

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import com.aeliavision.androiddeck.core.ui.theme.AppElevation
import com.aeliavision.androiddeck.core.ui.theme.AppSpacing

/** Standard content surface for AndroidDeck feature summaries and panels. */
@Composable
fun AppCard(
    modifier: Modifier = Modifier,
    containerColor: Color = MaterialTheme.colorScheme.surface,
    contentPadding: PaddingValues = PaddingValues(AppSpacing.Lg),
    content: @Composable ColumnScope.() -> Unit
) {
    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = MaterialTheme.shapes.large,
        color = containerColor,
        tonalElevation = AppElevation.Low,
        border = null
    ) {
        Column(
            modifier = Modifier.padding(contentPadding),
            content = content
        )
    }
}
