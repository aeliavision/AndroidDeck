package com.aeliavision.androiddeck.feature.contacts.model

import androidx.annotation.Keep

/**
 * Data transfer objects matching the API contract.
 * Moved from com.androiddeck.companion.model to the contacts feature package.
 *
 * AND-L03 FIX: @Keep prevents R8/ProGuard from stripping or renaming these
 * classes — they are serialised/deserialised by Gson via reflection, so any
 * renaming would silently break JSON parsing in release builds.
 */
@Keep
// fields break structural equality (equals/hashCode) when a field is mutated after
// construction, and cause unnecessary Compose recompositions because the snapshot system
// cannot detect field-level mutations on plain data class instances. Use copy() to
// produce modified instances instead of mutating in place.
data class ContactDto(
    val id: String? = null,
    val firstName: String? = null,
    val middleName: String? = null,
    val lastName: String? = null,
    val prefix: String? = null,
    val suffix: String? = null,
    val fullName: String = "",
    val organization: String? = null,
    val title: String? = null,
    val emails: List<EmailDto>? = null,
    val phones: List<PhoneDto>? = null,
    val accountName: String? = null,
    val accountType: String? = null,
    val readOnly: Boolean = false,
    val etag: String? = null
)

@Keep
data class PhoneDto(
    val value: String,
    // SER-01 FIX: Changed from Int to String so the JSON wire type matches the desktop
    // ContactDto.PhoneDto.Type (string). The old Int field caused Gson to throw a
    // NumberFormatException when the desktop sent a JSON string like "2" in a create/update
    // request because Gson's default TypeAdapter for Int rejects non-numeric tokens.
    // Changing to String is lossless — Android ContactsContract type constants are small
    // integers that round-trip perfectly as strings ("1", "2", "3" ...).
    // ContactsRepository.createContact() and updateContact() parse back to Int with
    // toIntOrNull() ?: 0 before writing to the ContentResolver.
    val type: String,
    val label: String? = null
)

@Keep
data class EmailDto(
    val value: String,
    // SER-01 FIX: Same Int->String change for consistency and symmetry with PhoneDto.
    val type: String
)

@Keep
data class DeviceStatusDto(
    val deviceName: String,
    val androidVersion: Int,
    val accounts: List<String>,
    val writeSupported: Boolean
)

@Keep
data class PairRequest(
    val pairingCode: String,
    val clientId: String
)

@Keep
data class PairResponse(
    val sessionId: String,
    val hmacSecret: String,
    val expiresAt: Long
)

/**
 * so the desktop can verify the server's identity out-of-band.
 */
@Keep
data class PairResponseV2(
    val sessionId: String,
    val hmacSecret: String,
    val expiresAt: Long,
    /** Server's ephemeral ECDH public key (Base64 DER). Null if ECDH not used. */
    val serverPublicKey: String? = null,
    /** SHA-256 fingerprint of the server's TLS certificate, colon-separated hex (P2.5). */
    val certFingerprint: String? = null
)



@Keep
data class PairRequestV3(
    val pairingCode: String,
    val clientId: String,
    val clientPublicKey: String
)

@Keep
data class PairResponseV3(
    val sessionId: String,
    val expiresAt: Long,
    val serverPublicKey: String,
    val certFingerprint: String
)

@Keep
data class ContactsPage(
    val items: List<ContactDto>,
    val nextPage: Int?
)

@Keep
data class ContactsBatchRequest(
    val ids: List<String>
)

@Keep
data class ApiError(
    val error: String,
    val message: String
)

// SER-03 FIX: Typed response envelope for GET /status — replaces the untyped
// mapOf("deviceName" to deviceName) that Gson serialised as Map<String,Any?>.
// Using a data class guarantees a stable, documented JSON contract and eliminates
// the risk of typos in map keys silently producing wrong field names on the wire.
@Keep
data class StatusResponse(
    val deviceName: String,
    val supportsFiles: Boolean = false,
    val supportsGallery: Boolean = false,
    val supportsBackup: Boolean = false,
    // Permission requirements (helps desktop show a useful CTA)
    val requiresAllFilesAccess: Boolean = false,
    val requiresMediaPermissions: Boolean = false,
    val pairingProtocolVersion: Int = 3,
    val legacyPairingEnabled: Boolean = false
)

// SER-03 FIX: Typed error envelope for all error responses — replaces ad-hoc
// mapOf("error" to "...") literals scattered across route handlers.
@Keep
data class ErrorResponse(
    val error: String,
    val message: String? = null
)

@Keep
data class GroupDto(
    val id: String,
    val title: String,
    val accountName: String?,
    val accountType: String?,
    val memberCount: Int = 0
)

@Keep
data class GroupsPage(
    val items: List<GroupDto>
)

@Keep
data class DuplicateGroup(
    val type: String, // "name" or "phone"
    val value: String,
    val contactIds: List<String>,
    val contacts: List<ContactDto> = emptyList()
)

/**
 * Result of validating an incoming [ContactDto].
 */
sealed class ValidationResult {
    object Ok : ValidationResult()
    data class Invalid(val errors: List<String>) : ValidationResult()
}

/**
 * repository. Previously any malformed payload (missing names, blank phone numbers,
 * malformed email addresses, oversized strings) was passed directly to the
 * ContentResolver which either silently ignored it or threw an undocumented exception
 * that surfaced as a 500 Internal Server Error to the desktop client.
 *
 * Rules enforced:
 *  • At least one of firstName, lastName, or fullName must be non-blank.
 *  • Each phone value must be non-blank and contain at least one digit.
 *  • Each email address must be non-blank and contain '@'.
 *  • No individual string field may exceed 500 characters (ContentResolver limit).
 */
fun ContactDto.validate(): ValidationResult {
    val errors = mutableListOf<String>()

    // Identity: require at least one name field.
    val hasName = !firstName.isNullOrBlank() || !lastName.isNullOrBlank() || fullName.isNotBlank()
    if (!hasName) errors.add("At least one of firstName, lastName, or fullName is required.")

    // Phone numbers.
    phones?.forEachIndexed { i, phone ->
        if (phone.value.isBlank())
            errors.add("phones[$i].value must not be blank.")
        else if (phone.value.none { it.isDigit() })
            errors.add("phones[$i].value '${phone.value}' contains no digits.")
        if (phone.value.length > 500)
            errors.add("phones[$i].value exceeds 500 characters.")
    }

    // Email addresses.
    emails?.forEachIndexed { i, email ->
        if (email.value.isBlank())
            errors.add("emails[$i].value must not be blank.")
        else if (!email.value.contains('@'))
            errors.add("emails[$i].value '${email.value}' is not a valid email address.")
        if (email.value.length > 500)
            errors.add("emails[$i].value exceeds 500 characters.")
    }

    // String length guards.
    mapOf(
        "fullName"     to fullName,
        "firstName"    to firstName,
        "lastName"     to lastName,
        "middleName"   to middleName,
        "prefix"       to prefix,
        "suffix"       to suffix,
        "organization" to organization,
        "title"        to title
    ).forEach { (field, value) ->
        if ((value?.length ?: 0) > 500)
            errors.add("$field exceeds 500 characters.")
    }

    return if (errors.isEmpty()) ValidationResult.Ok
    else ValidationResult.Invalid(errors)
}
