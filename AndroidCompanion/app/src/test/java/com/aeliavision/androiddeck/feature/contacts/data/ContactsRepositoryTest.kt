package com.aeliavision.androiddeck.feature.contacts.data

import android.content.ContentResolver
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertNotNull
import org.junit.Before
import org.junit.Test
import org.mockito.Mock
import org.mockito.MockitoAnnotations

/**
 * Unit tests for the Contacts Repository.
 */
class ContactsRepositoryTest {

    @Mock
    private lateinit var contentResolver: ContentResolver

    private lateinit var repository: ContactsRepository

    @Before
    fun setup() {
        MockitoAnnotations.openMocks(this)
        repository = ContactsRepository(contentResolver)
    }

    @Test
    fun repository_isInitialized() = runTest {
        assertNotNull(repository)
    }
}
