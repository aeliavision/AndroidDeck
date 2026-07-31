package com.aeliavision.androiddeck.feature.contacts.ui.shared

/**
 * Centralized type label mapping to avoid magic numbers scattered across the UI.
 * Types are stringified integer constants coming from ContactsContract.
 */
object ContactTypeLabels {
    const val PHONE_HOME = "1"
    const val PHONE_MOBILE = "2"
    const val PHONE_WORK = "3"
    const val PHONE_OTHER = "7"

    const val EMAIL_HOME = "1"
    const val EMAIL_WORK = "2"
    const val EMAIL_OTHER = "3"

    val phoneTypes: List<Pair<String, String>> = listOf(
        PHONE_HOME to "Home",
        PHONE_MOBILE to "Mobile",
        PHONE_WORK to "Work",
        PHONE_OTHER to "Other"
    )

    val emailTypes: List<Pair<String, String>> = listOf(
        EMAIL_HOME to "Home",
        EMAIL_WORK to "Work",
        EMAIL_OTHER to "Other"
    )
}

fun phoneTypeLabel(type: String, label: String?): String {
    if (!label.isNullOrBlank()) return label
    return ContactTypeLabels.phoneTypes.firstOrNull { it.first == type }?.second ?: "Other"
}

fun emailTypeLabel(type: String): String {
    return ContactTypeLabels.emailTypes.firstOrNull { it.first == type }?.second ?: "Other"
}
