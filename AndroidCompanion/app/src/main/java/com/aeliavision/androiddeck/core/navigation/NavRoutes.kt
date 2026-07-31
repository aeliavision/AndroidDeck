package com.aeliavision.androiddeck.core.navigation

import kotlinx.serialization.Serializable
import androidx.navigation3.runtime.NavKey

@Serializable
object Dashboard : NavKey

@Serializable
object ContactList : NavKey

@Serializable
data class ContactDetail(val id: String) : NavKey

@Serializable
data class ContactEdit(val id: String) : NavKey

@Serializable
object ContactNew : NavKey

@Serializable
object Groups : NavKey

@Serializable
data class GroupContacts(val groupId: String) : NavKey

@Serializable
object MediaHub : NavKey

@Serializable
object Server : NavKey

@Serializable
object Settings : NavKey

@Serializable
object Cleanup : NavKey
