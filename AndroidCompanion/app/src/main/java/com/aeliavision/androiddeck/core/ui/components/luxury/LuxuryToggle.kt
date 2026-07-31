package com.aeliavision.androiddeck.core.ui.components.luxury

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier

/**
 * Compatibility wrapper retained for older screens and stale working copies.
 * New code should use SettingToggle from the semantic component package.
 */
@Deprecated(
    message = "Use SettingToggle or Material3 Switch",
    replaceWith = ReplaceWith("Switch(checked, onCheckedChange, modifier)")
)
@Composable
public fun LuxuryToggle(
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true
): Unit {
    Switch(
        checked = checked,
        onCheckedChange = onCheckedChange,
        modifier = modifier,
        enabled = enabled,
        colors = SwitchDefaults.colors(
            checkedThumbColor = MaterialTheme.colorScheme.onPrimary,
            checkedTrackColor = MaterialTheme.colorScheme.primary,
            uncheckedThumbColor = MaterialTheme.colorScheme.outline,
            uncheckedTrackColor = MaterialTheme.colorScheme.surfaceVariant
        )
    )
}
