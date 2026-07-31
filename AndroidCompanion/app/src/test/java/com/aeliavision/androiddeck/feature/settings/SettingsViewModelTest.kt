package com.aeliavision.androiddeck.feature.settings

import com.aeliavision.androiddeck.data.AuthPreferencesStore
import com.aeliavision.androiddeck.feature.server.service.AuthManager
import com.aeliavision.androiddeck.feature.settings.viewmodel.SettingsViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.mockito.Mock
import org.mockito.Mockito.`when`
import org.mockito.Mockito.verify
import org.mockito.MockitoAnnotations

@OptIn(ExperimentalCoroutinesApi::class)
public class SettingsViewModelTest {

    @Mock
    private lateinit var preferencesStore: AuthPreferencesStore

    @Mock
    private lateinit var authManager: AuthManager

    private val testDispatcher = UnconfinedTestDispatcher()
    private lateinit var viewModel: SettingsViewModel

    @Before
    public fun setup() {
        MockitoAnnotations.openMocks(this)
        Dispatchers.setMain(testDispatcher)

        `when`(preferencesStore.darkMode).thenReturn(kotlinx.coroutines.flow.MutableStateFlow("system"))
        `when`(preferencesStore.serverPort).thenReturn(kotlinx.coroutines.flow.MutableStateFlow(8732))
        `when`(authManager.sessionCount).thenReturn(kotlinx.coroutines.flow.MutableStateFlow(0))
        `when`(authManager.getSessionsList()).thenReturn(emptyList())

        viewModel = SettingsViewModel(preferencesStore, authManager)
        testDispatcher.scheduler.advanceUntilIdle()
    }

    @After
    public fun tearDown() {
        Dispatchers.resetMain()
    }

    @Test
    public fun setServerPort_rejectsInvalidPortValues() = runTest(testDispatcher) {
        backgroundScope.launch { viewModel.uiState.collect {} }
        testDispatcher.scheduler.advanceUntilIdle()

        val resultLow = viewModel.setServerPort("80")
        assertFalse(resultLow)
        testDispatcher.scheduler.advanceUntilIdle()
        assertEquals("Port must be a valid number between 1024 and 65535", viewModel.uiState.value.portError)

        val resultHigh = viewModel.setServerPort("70000")
        assertFalse(resultHigh)

        val resultNonNumeric = viewModel.setServerPort("invalid")
        assertFalse(resultNonNumeric)
    }

    @Test
    public fun setServerPort_acceptsValidPortValues() = runTest(testDispatcher) {
        backgroundScope.launch { viewModel.uiState.collect {} }
        testDispatcher.scheduler.advanceUntilIdle()

        val result = viewModel.setServerPort("9000")
        assertTrue(result)
        testDispatcher.scheduler.advanceUntilIdle()
        assertNull(viewModel.uiState.value.portError)
        verify(preferencesStore).setServerPort(9000)
    }

    @Test
    public fun clearSessions_callsRevokeAllSessionsOnAuthManager() = runTest(testDispatcher) {
        backgroundScope.launch { viewModel.uiState.collect {} }
        testDispatcher.scheduler.advanceUntilIdle()

        viewModel.clearSessions()
        testDispatcher.scheduler.advanceUntilIdle()
        verify(authManager).revokeAllSessions()
        verify(preferencesStore).setSessionId(null)
    }

}

