package com.aeliavision.androiddeck.core.ui.theme

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Shapes
import androidx.compose.ui.unit.dp

val AppShapes = Shapes(
    extraSmall = RoundedCornerShape(AppRadii.Xs),
    small = RoundedCornerShape(AppRadii.Sm),
    medium = RoundedCornerShape(AppRadii.Md),
    large = RoundedCornerShape(AppRadii.Lg),
    extraLarge = RoundedCornerShape(16.dp)
)
