package com.aeliavision.androiddeck.core.navigation

import androidx.activity.compose.BackHandler
import androidx.annotation.StringRes
import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.adaptive.navigationsuite.NavigationSuiteDefaults
import androidx.compose.material3.adaptive.navigationsuite.NavigationSuiteScaffold
import androidx.compose.material3.windowsizeclass.WindowSizeClass
import androidx.compose.runtime.Composable
import androidx.compose.ui.res.stringResource
import androidx.navigation3.runtime.NavEntry
import androidx.navigation3.runtime.NavKey
import androidx.navigation3.runtime.rememberNavBackStack
import androidx.navigation3.ui.NavDisplay
import com.aeliavision.androiddeck.R
import com.aeliavision.androiddeck.feature.contacts.ui.cleanup.CleanupScreen
import com.aeliavision.androiddeck.feature.contacts.ui.detail.ContactDetailScreen
import com.aeliavision.androiddeck.feature.contacts.ui.edit.EditContactScreen
import com.aeliavision.androiddeck.feature.contacts.ui.groups.GroupsScreen
import com.aeliavision.androiddeck.feature.contacts.ui.list.ContactListScreen
import com.aeliavision.androiddeck.feature.dashboard.ui.DashboardScreen
import com.aeliavision.androiddeck.feature.media.ui.MediaHubScreen
import com.aeliavision.androiddeck.feature.server.ui.ServerScreen
import com.aeliavision.androiddeck.feature.settings.ui.SettingsScreen
import kotlin.reflect.KClass

/** Tab descriptor for Nav3 using string resources */
private data class TabInfo<T : NavKey>(
    val key: T,
    @StringRes val labelRes: Int,
    val icon: androidx.compose.ui.graphics.vector.ImageVector,
    val keyClass: KClass<out T>
)

private val topLevelTabs = listOf(
    TabInfo(Dashboard, R.string.tab_dashboard, Icons.Outlined.Dashboard, Dashboard::class),
    TabInfo(ContactList, R.string.tab_contacts, Icons.Outlined.Contacts, ContactList::class),
    TabInfo(MediaHub, R.string.tab_media, Icons.Outlined.PhotoLibrary, MediaHub::class),
    TabInfo(Server, R.string.tab_server, Icons.Outlined.Wifi, Server::class),
    TabInfo(Settings, R.string.tab_settings, Icons.Outlined.Settings, Settings::class)
)

/**
 * Top-level navigation host using Navigation 3 (2026 standard)
 * and Material 3 Adaptive Navigation Suite.
 */
@Composable
fun AppNavHost(windowSizeClass: WindowSizeClass) {
    val backStack = rememberNavBackStack(Dashboard)
    val currentKey = backStack.lastOrNull() ?: Dashboard

    // Helper to determine which top-level tab is currently active
    fun isTabSelected(tab: TabInfo<*>): Boolean {
        return when (currentKey) {
            is Dashboard -> tab.key is Dashboard
            is ContactList, is ContactDetail, is ContactEdit, is ContactNew, is Groups, is GroupContacts, is Cleanup -> tab.key is ContactList
            is MediaHub -> tab.key is MediaHub
            is Server -> tab.key is Server
            is Settings -> tab.key is Settings
            else -> false
        }
    }

    // Predictive / system back handling
    BackHandler(enabled = backStack.size > 1) {
        backStack.removeAt(backStack.size - 1)
    }

    fun navigateTo(key: NavKey) {
        backStack.add(key)
    }

    NavigationSuiteScaffold(
        navigationSuiteItems = {
            topLevelTabs.forEach { tab ->
                item(
                    icon = { Icon(tab.icon, contentDescription = stringResource(tab.labelRes)) },
                    label = { Text(stringResource(tab.labelRes)) },
                    selected = isTabSelected(tab),
                    onClick = {
                        if (!isTabSelected(tab)) {
                            // Switch top-level tab
                            backStack.clear()
                            backStack.add(tab.key)
                        } else if (backStack.size > 1) {
                            // Re-clicking selected tab pops detail screens to root
                            backStack.clear()
                            backStack.add(tab.key)
                        }
                    }
                )
            }
        },
        navigationSuiteColors = NavigationSuiteDefaults.colors(
            navigationBarContentColor = MaterialTheme.colorScheme.primary,
            navigationRailContentColor = MaterialTheme.colorScheme.primary
        )
    ) {
        NavDisplay(
            backStack = backStack,
            onBack = {
                if (backStack.size > 1) {
                    backStack.removeAt(backStack.size - 1)
                }
            },
            transitionSpec = {
                fadeIn(tween(300)) +
                        slideInHorizontally(
                            initialOffsetX = { it / 20 },
                            animationSpec = tween(300)
                        ) togetherWith
                        fadeOut(tween(250))
            }
        ) { key ->
            NavEntry(key) {
                when (key) {
                    is Dashboard -> DashboardScreen()
                    is ContactList -> ContactListScreen(
                        onContactClick = { id -> navigateTo(ContactDetail(id)) },
                        onAddContact = { navigateTo(ContactNew) },
                        onCleanupClick = { navigateTo(Cleanup) },
                        onGroupsClick = { navigateTo(Groups) },
                        onBack = null
                    )
                    is ContactDetail -> ContactDetailScreen(
                        contactId = key.id,
                        onEdit = { navigateTo(ContactEdit(key.id)) },
                        onBack = { if (backStack.size > 1) backStack.removeAt(backStack.size - 1) }
                    )
                    is ContactEdit -> EditContactScreen(
                        contactId = key.id,
                        onSaved = { if (backStack.size > 1) backStack.removeAt(backStack.size - 1) },
                        onBack = { if (backStack.size > 1) backStack.removeAt(backStack.size - 1) }
                    )
                    is ContactNew -> EditContactScreen(
                        contactId = null,
                        onSaved = { if (backStack.size > 1) backStack.removeAt(backStack.size - 1) },
                        onBack = { if (backStack.size > 1) backStack.removeAt(backStack.size - 1) }
                    )
                    is Groups -> GroupsScreen(
                        onGroupSelected = { groupId, _ ->
                            navigateTo(GroupContacts(groupId))
                        },
                        onBack = { if (backStack.size > 1) backStack.removeAt(backStack.size - 1) }
                    )
                    is GroupContacts -> ContactListScreen(
                        onContactClick = { id -> navigateTo(ContactDetail(id)) },
                        onAddContact = { navigateTo(ContactNew) },
                        onCleanupClick = { navigateTo(Cleanup) },
                        onGroupsClick = { navigateTo(Groups) },
                        filterGroupId = key.groupId,
                        onBack = { if (backStack.size > 1) backStack.removeAt(backStack.size - 1) }
                    )
                    is MediaHub -> MediaHubScreen()
                    is Server -> ServerScreen()
                    is Settings -> SettingsScreen()
                    is Cleanup -> CleanupScreen(
                        onBack = { if (backStack.size > 1) backStack.removeAt(backStack.size - 1) }
                    )
                }
            }
        }
    }
}
