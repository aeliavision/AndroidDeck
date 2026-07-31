package com.aeliavision.androiddeck.di

import android.content.ContentResolver
import com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
object ContactsModule {

    @Provides
    @Singleton
    fun provideContactsRepository(contentResolver: ContentResolver): ContactsRepository =
        ContactsRepository(contentResolver)
}
