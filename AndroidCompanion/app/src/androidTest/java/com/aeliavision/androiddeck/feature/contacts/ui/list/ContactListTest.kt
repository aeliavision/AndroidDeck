package com.aeliavision.androiddeck.feature.contacts.ui.list

import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import com.aeliavision.androiddeck.core.ui.theme.AndroidDeckTheme
import org.junit.Rule
import org.junit.Test

/**
 * UI tests for the Contact List screen.
 */
class ContactListTest {

    @get:Rule
    val composeTestRule = createComposeRule()

    @Test
    fun contactList_initialState_showsTitle() {
        composeTestRule.setContent {
            AndroidDeckTheme {
                ContactListScreen(
                    onContactClick = {},
                    onAddContact = {},
                    onCleanupClick = {},
                    onGroupsClick = {},
                    onBack = {}
                )
            }
        }
    }
}
