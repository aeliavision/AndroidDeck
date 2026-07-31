package com.aeliavision.androiddeck.feature.media.ui

import androidx.compose.animation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Folder
import androidx.compose.material.icons.outlined.PhotoLibrary
import androidx.compose.material3.*
import androidx.compose.material3.SecondaryTabRow
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.aeliavision.androiddeck.feature.filesystem.ui.FilesScreen
import com.aeliavision.androiddeck.feature.gallery.ui.GalleryScreen

/**
 * Modern Media Hub that combines Gallery and File Browser.
 */
@Composable
fun MediaHubScreen() {
    var selectedTab by remember { mutableIntStateOf(0) }
    val tabs = listOf("Gallery", "Files")
    val icons = listOf(Icons.Outlined.PhotoLibrary, Icons.Outlined.Folder)

    Column(modifier = Modifier.fillMaxSize()) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 16.dp, bottom = 8.dp),
            contentAlignment = Alignment.Center
        ) {
            Surface(
                color = Color.White.copy(alpha = 0.05f),
                shape = CircleShape,
                modifier = Modifier.padding(horizontal = 24.dp)
            ) {
                SecondaryTabRow(
                    selectedTabIndex = selectedTab,
                    modifier = Modifier.width(280.dp),
                    containerColor = Color.Transparent,
                    contentColor = MaterialTheme.colorScheme.primary,
                    indicator = {}, // Custom rounded indicator
                    divider = {},
                    tabs = {
                        tabs.forEachIndexed { index, title ->
                            val isSelected = selectedTab == index
                            Tab(
                                selected = isSelected,
                                onClick = { selectedTab = index },
                                modifier = Modifier
                                    .padding(4.dp)
                                    .height(40.dp),
                                selectedContentColor = MaterialTheme.colorScheme.primary,
                                unselectedContentColor = Color.White.copy(alpha = 0.4f)
                            ) {
                                Row(
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.Center
                                ) {
                                    Icon(
                                        imageVector = icons[index],
                                        contentDescription = null,
                                        modifier = Modifier.size(18.dp)
                                    )
                                    Spacer(Modifier.width(8.dp))
                                    Text(
                                        text = title,
                                        style = MaterialTheme.typography.labelLarge
                                    )
                                }
                            }
                        }
                    })
            }
        }
        AnimatedContent(
            targetState = selectedTab,
            label = "MediaContent",
            transitionSpec = {
                fadeIn() togetherWith fadeOut()
            },
            modifier = Modifier.fillMaxSize()
        ) { tabIndex ->
            when (tabIndex) {
                0 -> GalleryScreen()
                1 -> FilesScreen()
            }
        }
    }
}
