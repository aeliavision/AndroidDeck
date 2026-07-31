package com.aeliavision.androiddeck.di

import com.aeliavision.androiddeck.feature.dashboard.data.ActivityLogRepository
import com.aeliavision.androiddeck.feature.server.data.ServerManager
import com.aeliavision.androiddeck.feature.server.service.AuthManager
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
object ServerModule {

    @Provides
    @Singleton
    fun provideServerManager(activityLogRepository: ActivityLogRepository): ServerManager = 
        ServerManager(activityLogRepository)

    @Provides
    @Singleton
    fun provideAuthManager(): AuthManager = AuthManager()
}
