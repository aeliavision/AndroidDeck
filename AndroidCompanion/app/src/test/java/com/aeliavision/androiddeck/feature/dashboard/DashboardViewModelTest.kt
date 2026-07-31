package com.aeliavision.androiddeck.feature.dashboard

import com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
import com.aeliavision.androiddeck.feature.dashboard.data.ActivityLogRepository
import com.aeliavision.androiddeck.feature.dashboard.viewmodel.DashboardViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Before
import org.junit.Test
import org.mockito.Mock
import org.mockito.Mockito.`when`
import org.mockito.MockitoAnnotations

@OptIn(ExperimentalCoroutinesApi::class)
public class DashboardViewModelTest {

    @Mock
    private lateinit var contactsRepository: ContactsRepository

    @Mock
    private lateinit var activityLogRepository: ActivityLogRepository

    private val testDispatcher = StandardTestDispatcher()
    private lateinit var viewModel: DashboardViewModel

    @Before
    public fun setup() {
        MockitoAnnotations.openMocks(this)
        Dispatchers.setMain(testDispatcher)

        `when`(activityLogRepository.activities).thenReturn(MutableStateFlow(emptyList()))
    }

    @After
    public fun tearDown() {
        Dispatchers.resetMain()
    }

    @Test
    public fun refreshMetrics_updatesMetricsStateOnSuccess() = runTest(testDispatcher) {
        `when`(contactsRepository.getAllContactIds()).thenReturn(listOf("1", "2", "3"))

        viewModel = DashboardViewModel(contactsRepository, activityLogRepository)
        viewModel.ioDispatcher = testDispatcher
        viewModel.refreshMetrics()
        testDispatcher.scheduler.advanceUntilIdle()

        val state = viewModel.uiState.value
        assertFalse(state.isLoading)
        assertEquals(3, state.metrics.contactCount)
    }

}

