package com.aeliavision.androiddeck.core.ui.components

import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
// Coil 3.x moved to the coil3.* package namespace.
import coil3.compose.AsyncImage
import coil3.compose.AsyncImagePainter
import coil3.request.ImageRequest

/** Palette of distinct background colours used for initials avatars */
private val avatarColors = listOf(
    Color(0xFF1976D2), // Blue
    Color(0xFF388E3C), // Green
    Color(0xFFF57C00), // Orange
    Color(0xFF7B1FA2), // Purple
    Color(0xFFC62828), // Red
    Color(0xFF00838F), // Teal
    Color(0xFF5D4037), // Brown
    Color(0xFF00695C), // Dark teal
    Color(0xFF283593), // Dark blue
    Color(0xFF558B2F), // Light green
)

/**
 * Circular avatar that shows the contact's real photo if available (loaded via Coil
 * from a [photoUri] or [photoBytes]), falling back to initials derived from [name].
 *
 * Usage:
 * - Pass [photoUri] (a `ContactsContract` content URI) for live device photos.
 * - Pass [photoBytes] for in-memory JPEG bytes received from the HTTP server.
 * - Leave both null to show initials only.
 */
@Composable
fun ContactAvatar(
    name: String,
    modifier: Modifier = Modifier,
    size: Dp = 48.dp,
    photoUri: Uri? = null,
    photoBytes: ByteArray? = null
) {
    val context = LocalContext.current

    // Build the Coil image request — prefer bytes, then URI.
    val model = remember(photoUri, photoBytes) {
        when {
            photoBytes != null -> ImageRequest.Builder(context)
                .data(photoBytes)
                .build()
            photoUri != null -> ImageRequest.Builder(context)
                .data(photoUri)
                .build()
            else -> null
        }
    }

    if (model != null) {
        // Subcomposition is expensive and causes jank in LazyColumns.
        // We place it in a Box with the initials behind it so there's always 
        // something to show while loading or if it fails.
        Box(
            modifier = modifier
                .size(size)
                .clip(CircleShape)
                .border(
                    width = 1.dp,
                    color = MaterialTheme.colorScheme.outline.copy(alpha = 0.5f),
                    shape = CircleShape
                ),
            contentAlignment = Alignment.Center
        ) {
            InitialsAvatar(name = name, size = size)
            AsyncImage(
                model = model,
                contentDescription = if (name.isBlank()) "Contact avatar" else "$name's photo",
                contentScale = ContentScale.Crop,
                modifier = Modifier.size(size)
            )
        }
    } else {
        InitialsAvatar(name = name, size = size, modifier = modifier)
    }
}

/**
 * Simple initials-only avatar. Use [ContactAvatar] when you have a photo source.
 */
@Composable
fun InitialsAvatar(
    name: String,
    modifier: Modifier = Modifier,
    size: Dp = 48.dp
) {
    val initials = remember(name) { extractInitials(name) }
    val backgroundColor = remember(name) {
        if (name.isBlank()) Color.Gray
        else avatarColors[Math.abs(name.hashCode()) % avatarColors.size]
    }
    val fontSize = remember(size) { (size.value * 0.38f).sp }

    Box(
        modifier = modifier
            .size(size)
            .clip(CircleShape)
            .background(backgroundColor)
            .border(
                width = 1.dp,
                color = MaterialTheme.colorScheme.outline.copy(alpha = 0.5f),
                shape = CircleShape
            ),
        contentAlignment = Alignment.Center
    ) {
        Text(
            text = initials,
            color = Color.White,
            fontSize = fontSize,
            fontWeight = FontWeight.SemiBold,
            letterSpacing = 0.5.sp
        )
    }
}

private fun extractInitials(name: String): String {
    val parts = name.trim().split("\\s+".toRegex()).filter { it.isNotBlank() }
    return when {
        parts.isEmpty() -> "?"
        parts.size == 1 -> parts[0].take(2).uppercase()
        else -> "${parts.first().first()}${parts.last().first()}".uppercase()
    }
}
