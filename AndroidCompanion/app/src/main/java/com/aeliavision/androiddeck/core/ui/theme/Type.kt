package com.aeliavision.androiddeck.core.ui.theme

import androidx.compose.material3.Typography
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

private val AppFontFamily = FontFamily.SansSerif

val AppTypography = Typography(
    displayLarge = TextStyle(
        fontFamily = AppFontFamily,
        fontWeight = FontWeight.Bold,
        fontSize = AppTypographyTokens.Display,
        lineHeight = 40.sp
    ),
    headlineLarge = TextStyle(
        fontFamily = AppFontFamily,
        fontWeight = FontWeight.SemiBold,
        fontSize = AppTypographyTokens.Heading,
        lineHeight = 32.sp
    ),
    headlineMedium = TextStyle(
        fontFamily = AppFontFamily,
        fontWeight = FontWeight.SemiBold,
        fontSize = AppTypographyTokens.PageTitle,
        lineHeight = 36.sp
    ),
    titleLarge = TextStyle(
        fontFamily = AppFontFamily,
        fontWeight = FontWeight.SemiBold,
        fontSize = AppTypographyTokens.Title,
        lineHeight = 24.sp
    ),
    titleMedium = TextStyle(
        fontFamily = AppFontFamily,
        fontWeight = FontWeight.Medium,
        fontSize = AppTypographyTokens.BodyLarge,
        lineHeight = 24.sp
    ),
    bodyLarge = TextStyle(
        fontFamily = AppFontFamily,
        fontWeight = FontWeight.Normal,
        fontSize = AppTypographyTokens.BodyLarge,
        lineHeight = 24.sp
    ),
    bodyMedium = TextStyle(
        fontFamily = AppFontFamily,
        fontWeight = FontWeight.Normal,
        fontSize = AppTypographyTokens.Body,
        lineHeight = AppTypographyTokens.BodyLineHeight
    ),
    bodySmall = TextStyle(
        fontFamily = AppFontFamily,
        fontWeight = FontWeight.Normal,
        fontSize = AppTypographyTokens.Caption,
        lineHeight = 16.sp
    ),
    labelLarge = TextStyle(
        fontFamily = AppFontFamily,
        fontWeight = FontWeight.Medium,
        fontSize = AppTypographyTokens.Body,
        lineHeight = AppTypographyTokens.BodyLineHeight
    ),
    labelMedium = TextStyle(
        fontFamily = AppFontFamily,
        fontWeight = FontWeight.Medium,
        fontSize = AppTypographyTokens.Caption,
        lineHeight = 16.sp
    )
)
