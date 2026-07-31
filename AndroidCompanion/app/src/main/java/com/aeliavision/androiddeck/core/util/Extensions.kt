package com.aeliavision.androiddeck.core.util

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.net.toUri

/** Launch the system dialler with the given [phoneNumber]. */
fun Context.dialPhone(phoneNumber: String) {
    val intent = Intent(Intent.ACTION_DIAL, "tel:${Uri.encode(phoneNumber)}".toUri())
    startActivity(intent)
}

/** Open the default email app pre-filled with [emailAddress]. */
fun Context.sendEmail(emailAddress: String) {
    val intent = Intent(Intent.ACTION_SENDTO, "mailto:${Uri.encode(emailAddress)}".toUri())
    startActivity(intent)
}

/** Capitalise the first character of a string safely. */
fun String.capitalizeFirst(): String =
    if (isEmpty()) this else this[0].uppercaseChar() + substring(1)
