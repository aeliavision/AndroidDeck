package com.aeliavision.androiddeck.feature.contacts.data

import android.content.ContentResolver
import android.content.ContentProviderOperation
import android.content.ContentUris
import android.database.Cursor
import android.net.Uri
import android.os.Bundle
import android.provider.ContactsContract
import android.provider.ContactsContract.CommonDataKinds
import android.util.Log
import com.aeliavision.androiddeck.feature.contacts.model.ContactDto
import com.aeliavision.androiddeck.feature.contacts.model.GroupDto
import com.aeliavision.androiddeck.feature.contacts.model.EmailDto
import com.aeliavision.androiddeck.feature.contacts.model.PhoneDto
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import javax.inject.Inject
import javax.inject.Singleton
import java.io.InputStream
import java.io.OutputStream
import java.util.concurrent.atomic.AtomicReference

class ReadOnlyAccountException(message: String) : Exception(message)

data class ImportResult(val total: Int, val imported: Int, val failed: Int, val skipped: Int = 0)

/**
 * Contacts feature data layer.
 * All ContentResolver calls run on Dispatchers.IO, never on the main thread.
 */
@Singleton
class ContactsRepository @Inject constructor(
    private val contentResolver: ContentResolver
) {
    companion object {
        private const val TAG = "ContactsRepository"
        private const val DEFAULT_PAGE_SIZE = 50
        private const val CACHE_TTL_MS = 30_000L
        private val READ_ONLY_ACCOUNT_TYPES = setOf(
            "com.whatsapp",
            "com.facebook.auth.login",
            "com.viber.voip",
            "org.telegram.messenger",
            "com.google.android.apps.tachyon",
            "com.linkedin.android",
            "com.twitter.android.auth.login"
        )
    }

    private val cacheRef = AtomicReference<Pair<List<ContactDto>, Long>?>(null)

    private fun invalidateCache() {
        cacheRef.set(null)
    }

    suspend fun getContacts(
        page: Int = 1,
        pageSize: Int = DEFAULT_PAGE_SIZE,
        query: String? = null
    ): List<ContactDto> = withContext(Dispatchers.IO) {
        if (page == 1 && query.isNullOrBlank()) {
            val snapshot = cacheRef.get()
            if (snapshot != null && (System.currentTimeMillis() - snapshot.second) < CACHE_TTL_MS) {
                return@withContext snapshot.first
            }
        }
        val contacts = mutableListOf<ContactDto>()
        val safePage = maxOf(page, 1)
        val safePageSize = pageSize.coerceIn(1, 200)
        val offset = (safePage - 1) * safePageSize

        val selection = if (!query.isNullOrBlank())
            "${ContactsContract.Contacts.DISPLAY_NAME_PRIMARY} LIKE ?" else null
        val selectionArgs = if (!query.isNullOrBlank()) arrayOf("%$query%") else null

        val queryArgs = Bundle().apply {
            putStringArray(
                ContentResolver.QUERY_ARG_SORT_COLUMNS,
                arrayOf(ContactsContract.Contacts.DISPLAY_NAME_PRIMARY)
            )
            putInt(ContentResolver.QUERY_ARG_SORT_DIRECTION, ContentResolver.QUERY_SORT_DIRECTION_ASCENDING)
            putInt(ContentResolver.QUERY_ARG_LIMIT, safePageSize)
            putInt(ContentResolver.QUERY_ARG_OFFSET, offset)
            if (selection != null) putString(ContentResolver.QUERY_ARG_SQL_SELECTION, selection)
            if (selectionArgs != null) putStringArray(ContentResolver.QUERY_ARG_SQL_SELECTION_ARGS, selectionArgs)
        }

        val cursor: Cursor? = contentResolver.query(
            ContactsContract.Contacts.CONTENT_URI,
            arrayOf(
                ContactsContract.Contacts._ID,
                ContactsContract.Contacts.DISPLAY_NAME_PRIMARY
            ),
            queryArgs,
            null
        )

        val contactIds = mutableListOf<String>()
        val fallbackNames = mutableMapOf<String, String>()

        cursor?.use {
            val totalRows = it.count
            val providerHonouredLimit = totalRows <= safePageSize

            if (providerHonouredLimit) {
                while (it.moveToNext()) {
                    val id = it.getString(it.getColumnIndexOrThrow(ContactsContract.Contacts._ID))
                    val displayName = it.getString(it.getColumnIndexOrThrow(ContactsContract.Contacts.DISPLAY_NAME_PRIMARY))
                    contactIds.add(id)
                    fallbackNames[id] = displayName ?: ""
                }
            } else {
                val startPos = offset
                if (startPos < totalRows && it.moveToPosition(startPos)) {
                    var count = 0
                    do {
                        val id = it.getString(it.getColumnIndexOrThrow(ContactsContract.Contacts._ID))
                        val displayName = it.getString(it.getColumnIndexOrThrow(ContactsContract.Contacts.DISPLAY_NAME_PRIMARY))
                        contactIds.add(id)
                        fallbackNames[id] = displayName ?: ""
                        count++
                    } while (count < safePageSize && it.moveToNext())
                }
            }
        }

        // DISPLAY_NAME_PRIMARY is an aggregated column that Android updates asynchronously.
        // After a write via ContentProviderOperation (updateContact), the StructuredName.DISPLAY_NAME
        // in the Data table reflects the change immediately, while DISPLAY_NAME_PRIMARY can lag
        // by several seconds. Read StructuredName display names in a single Data-table query and
        // prefer them — this makes Refresh show the correct name right after an edit.
        if (contactIds.isNotEmpty()) {
            val placeholders = contactIds.joinToString(",") { "?" }
            val dataCursor: Cursor? = contentResolver.query(
                ContactsContract.Data.CONTENT_URI,
                arrayOf(
                    ContactsContract.Data.CONTACT_ID,
                    CommonDataKinds.StructuredName.DISPLAY_NAME
                ),
                "${ContactsContract.Data.CONTACT_ID} IN ($placeholders) AND " +
                    "${ContactsContract.Data.MIMETYPE} = ?",
                (contactIds + CommonDataKinds.StructuredName.CONTENT_ITEM_TYPE).toTypedArray(),
                null
            )
            val structuredNames = mutableMapOf<String, String>()
            dataCursor?.use { dc ->
                val contactIdIdx = dc.getColumnIndexOrThrow(ContactsContract.Data.CONTACT_ID)
                val nameIdx = dc.getColumnIndexOrThrow(CommonDataKinds.StructuredName.DISPLAY_NAME)
                while (dc.moveToNext()) {
                    val id = dc.getString(contactIdIdx)
                    val name = dc.getString(nameIdx)
                    if (!name.isNullOrBlank() && !structuredNames.containsKey(id)) {
                        structuredNames[id] = name
                    }
                }
            }

            for (id in contactIds) {
                // Prefer the synchronously-written StructuredName; fall back to the aggregated column.
                val resolvedName = structuredNames[id]?.takeIf { it.isNotBlank() }
                    ?: fallbackNames[id]
                    ?: ""
                contacts.add(ContactDto(id = id, fullName = resolvedName))
            }
        }

        if (page == 1 && query.isNullOrBlank()) {
            cacheRef.set(Pair(contacts, System.currentTimeMillis()))
        }
        contacts
    }

    suspend fun getContactDetail(contactId: String): ContactDto? = withContext(Dispatchers.IO) {
        Log.d(TAG, "Getting details for contactId: $contactId")

        var firstName: String? = null
        var middleName: String? = null
        var lastName: String? = null
        var prefix: String? = null
        var suffix: String? = null
        var fullName: String = ""
        var organization: String? = null
        var title: String? = null
        val phones = mutableListOf<PhoneDto>()
        val emails = mutableListOf<EmailDto>()

        val contactUri = Uri.withAppendedPath(ContactsContract.Contacts.CONTENT_URI, contactId)
        val dataUri = Uri.withAppendedPath(contactUri, ContactsContract.Contacts.Data.CONTENT_DIRECTORY)

        val dataCursor = contentResolver.query(dataUri, null, null, null, null)
        dataCursor?.use { cursor ->
            val mimetypeIdx = cursor.getColumnIndexOrThrow(ContactsContract.Data.MIMETYPE)
            while (cursor.moveToNext()) {
                when (cursor.getString(mimetypeIdx)) {
                    CommonDataKinds.StructuredName.CONTENT_ITEM_TYPE -> {
                        firstName = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.GIVEN_NAME))
                        middleName = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.MIDDLE_NAME))
                        lastName = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.FAMILY_NAME))
                        prefix = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.PREFIX))
                        suffix = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.SUFFIX))
                        val dn = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.DISPLAY_NAME))
                        if (!dn.isNullOrBlank()) fullName = dn
                    }
                    CommonDataKinds.Phone.CONTENT_ITEM_TYPE -> {
                        val number = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.Phone.NUMBER))
                        val type = cursor.getInt(cursor.getColumnIndexOrThrow(CommonDataKinds.Phone.TYPE)).toString()
                        if (number != null) {
                            val normalised = number.filter { it.isDigit() || it == '+' }
                            val alreadyExists = phones.any {
                                it.value.filter { c -> c.isDigit() || c == '+' } == normalised
                            }
                            if (!alreadyExists) {
                                phones.add(PhoneDto(value = number, type = type))
                            }
                        }
                    }
                    CommonDataKinds.Email.CONTENT_ITEM_TYPE -> {
                        val address = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.Email.ADDRESS))
                        val type = cursor.getInt(cursor.getColumnIndexOrThrow(CommonDataKinds.Email.TYPE)).toString()
                        if (address != null) {
                            val alreadyExists = emails.any {
                                it.value.equals(address, ignoreCase = true)
                            }
                            if (!alreadyExists) {
                                emails.add(EmailDto(value = address, type = type))
                            }
                        }
                    }
                    CommonDataKinds.Organization.CONTENT_ITEM_TYPE -> {
                        organization = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.Organization.COMPANY))
                        title = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.Organization.TITLE))
                    }
                }
            }
        }

        var accountName: String? = null
        var accountType: String? = null
        var etag: String? = null
        var readOnly = false

        val rawCursor = contentResolver.query(
            ContactsContract.RawContacts.CONTENT_URI,
            arrayOf(
                ContactsContract.RawContacts._ID,
                ContactsContract.RawContacts.ACCOUNT_NAME,
                ContactsContract.RawContacts.ACCOUNT_TYPE,
                ContactsContract.RawContacts.VERSION
            ),
            "${ContactsContract.RawContacts.CONTACT_ID} = ?",
            arrayOf(contactId), null
        )
        rawCursor?.use {
            var preferredAccountName: String? = null
            var preferredAccountType: String? = null
            var preferredEtag: String? = null
            var sawAny = false
            var allReadOnly = true

            val accountNameIdx = it.getColumnIndexOrThrow(ContactsContract.RawContacts.ACCOUNT_NAME)
            val accountTypeIdx = it.getColumnIndexOrThrow(ContactsContract.RawContacts.ACCOUNT_TYPE)
            val versionIdx = it.getColumnIndexOrThrow(ContactsContract.RawContacts.VERSION)

            while (it.moveToNext()) {
                sawAny = true
                val rawAccountName = it.getString(accountNameIdx)
                val rawAccountType = it.getString(accountTypeIdx)
                val rawEtag = it.getString(versionIdx)
                if (!isReadOnlyAccount(rawAccountType)) {
                    allReadOnly = false
                    if (preferredAccountType == null) {
                        preferredAccountName = rawAccountName
                        preferredAccountType = rawAccountType
                        preferredEtag = rawEtag
                    }
                }
            }
            if (sawAny && preferredAccountType == null) {
                it.moveToFirst()
                preferredAccountName = it.getString(accountNameIdx)
                preferredAccountType = it.getString(accountTypeIdx)
                preferredEtag = it.getString(versionIdx)
            }
            accountName = preferredAccountName
            accountType = preferredAccountType
            readOnly = sawAny && allReadOnly
            etag = preferredEtag
        }

        val dto = ContactDto(
            id = contactId,
            firstName = firstName,
            middleName = middleName,
            lastName = lastName,
            prefix = prefix,
            suffix = suffix,
            fullName = fullName,
            organization = organization,
            title = title,
            phones = phones.ifEmpty { null },
            emails = emails.ifEmpty { null },
            accountName = accountName,
            accountType = accountType,
            readOnly = readOnly,
            etag = etag
        )
        Log.d(TAG, "Finished: ${dto.fullName}, phones=${dto.phones?.size ?: 0}")
        dto
    }

    suspend fun getContactDetails(contactIds: List<String>): List<ContactDto> = withContext(Dispatchers.IO) {
        if (contactIds.isEmpty()) return@withContext emptyList()

        val uniqueIds = contactIds.distinct().take(500)
        val placeholders = uniqueIds.joinToString(",") { "?" }
        val selArgs = uniqueIds.toTypedArray()

        data class ContactBuilder(
            var firstName: String? = null,
            var middleName: String? = null,
            var lastName: String? = null,
            var prefix: String? = null,
            var suffix: String? = null,
            var fullName: String = "",
            var organization: String? = null,
            var title: String? = null,
            val phones: MutableList<PhoneDto> = mutableListOf(),
            val emails: MutableList<EmailDto> = mutableListOf()
        )

        data class RawAgg(
            var preferredAccountName: String? = null,
            var preferredAccountType: String? = null,
            var preferredEtag: String? = null,
            var sawAny: Boolean = false,
            var allReadOnly: Boolean = true
        )

        val builderMap = linkedMapOf<String, ContactBuilder>()
        val rawAggMap = linkedMapOf<String, RawAgg>()
        uniqueIds.forEach {
            builderMap[it] = ContactBuilder()
            rawAggMap[it] = RawAgg()
        }

        contentResolver.query(
            ContactsContract.Data.CONTENT_URI,
            arrayOf(
                ContactsContract.Data.CONTACT_ID,
                ContactsContract.Data.MIMETYPE,
                CommonDataKinds.StructuredName.GIVEN_NAME,
                CommonDataKinds.StructuredName.MIDDLE_NAME,
                CommonDataKinds.StructuredName.FAMILY_NAME,
                CommonDataKinds.StructuredName.PREFIX,
                CommonDataKinds.StructuredName.SUFFIX,
                CommonDataKinds.StructuredName.DISPLAY_NAME,
                CommonDataKinds.Phone.NUMBER,
                CommonDataKinds.Phone.TYPE,
                CommonDataKinds.Email.ADDRESS,
                CommonDataKinds.Email.TYPE,
                CommonDataKinds.Organization.COMPANY,
                CommonDataKinds.Organization.TITLE
            ),
            "${ContactsContract.Data.CONTACT_ID} IN ($placeholders)",
            selArgs,
            null
        )?.use { cursor ->
            val contactIdIdx = cursor.getColumnIndexOrThrow(ContactsContract.Data.CONTACT_ID)
            val mimeIdx = cursor.getColumnIndexOrThrow(ContactsContract.Data.MIMETYPE)

            while (cursor.moveToNext()) {
                val cid = cursor.getString(contactIdIdx) ?: continue
                val b = builderMap[cid] ?: continue
                when (cursor.getString(mimeIdx)) {
                    CommonDataKinds.StructuredName.CONTENT_ITEM_TYPE -> {
                        b.firstName = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.GIVEN_NAME))
                        b.middleName = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.MIDDLE_NAME))
                        b.lastName = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.FAMILY_NAME))
                        b.prefix = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.PREFIX))
                        b.suffix = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.SUFFIX))
                        val dn = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.StructuredName.DISPLAY_NAME))
                        if (!dn.isNullOrBlank()) b.fullName = dn
                    }
                    CommonDataKinds.Phone.CONTENT_ITEM_TYPE -> {
                        val number = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.Phone.NUMBER))
                        val type = cursor.getInt(cursor.getColumnIndexOrThrow(CommonDataKinds.Phone.TYPE)).toString()
                        if (!number.isNullOrBlank()) {
                            val norm = number.filter { it.isDigit() || it == '+' }
                            if (b.phones.none { it.value.filter { c -> c.isDigit() || c == '+' } == norm })
                                b.phones.add(PhoneDto(value = number, type = type))
                        }
                    }
                    CommonDataKinds.Email.CONTENT_ITEM_TYPE -> {
                        val address = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.Email.ADDRESS))
                        val type = cursor.getInt(cursor.getColumnIndexOrThrow(CommonDataKinds.Email.TYPE)).toString()
                        if (!address.isNullOrBlank()) {
                            if (b.emails.none { it.value.equals(address, ignoreCase = true) })
                                b.emails.add(EmailDto(value = address, type = type))
                        }
                    }
                    CommonDataKinds.Organization.CONTENT_ITEM_TYPE -> {
                        b.organization = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.Organization.COMPANY))
                        b.title = cursor.getString(cursor.getColumnIndexOrThrow(CommonDataKinds.Organization.TITLE))
                    }
                }
            }
        }

        contentResolver.query(
            ContactsContract.RawContacts.CONTENT_URI,
            arrayOf(
                ContactsContract.RawContacts.CONTACT_ID,
                ContactsContract.RawContacts.ACCOUNT_NAME,
                ContactsContract.RawContacts.ACCOUNT_TYPE,
                ContactsContract.RawContacts.VERSION
            ),
            "${ContactsContract.RawContacts.CONTACT_ID} IN ($placeholders)",
            selArgs,
            null
        )?.use { cursor ->
            val contactIdIdx = cursor.getColumnIndexOrThrow(ContactsContract.RawContacts.CONTACT_ID)
            val accountNameIdx = cursor.getColumnIndexOrThrow(ContactsContract.RawContacts.ACCOUNT_NAME)
            val accountTypeIdx = cursor.getColumnIndexOrThrow(ContactsContract.RawContacts.ACCOUNT_TYPE)
            val versionIdx = cursor.getColumnIndexOrThrow(ContactsContract.RawContacts.VERSION)

            while (cursor.moveToNext()) {
                val cid = cursor.getString(contactIdIdx) ?: continue
                val agg = rawAggMap[cid] ?: continue
                agg.sawAny = true

                val rawAccountName = cursor.getString(accountNameIdx)
                val rawAccountType = cursor.getString(accountTypeIdx)
                val rawEtag = cursor.getString(versionIdx)

                if (!isReadOnlyAccount(rawAccountType)) {
                    agg.allReadOnly = false
                    if (agg.preferredAccountType == null) {
                        agg.preferredAccountName = rawAccountName
                        agg.preferredAccountType = rawAccountType
                        agg.preferredEtag = rawEtag
                    }
                }
                if (agg.preferredAccountType == null && agg.preferredEtag == null) {
                    agg.preferredAccountName = rawAccountName
                    agg.preferredAccountType = rawAccountType
                    agg.preferredEtag = rawEtag
                }
            }
        }

        uniqueIds.mapNotNull { cid ->
            val b = builderMap[cid] ?: return@mapNotNull null
            val agg = rawAggMap[cid] ?: RawAgg()

            if (!agg.sawAny && b.fullName.isBlank() && b.phones.isEmpty() && b.emails.isEmpty() &&
                b.organization.isNullOrBlank() && b.title.isNullOrBlank()) {
                return@mapNotNull null
            }

            ContactDto(
                id = cid,
                firstName = b.firstName,
                middleName = b.middleName,
                lastName = b.lastName,
                prefix = b.prefix,
                suffix = b.suffix,
                fullName = b.fullName,
                organization = b.organization,
                title = b.title,
                phones = b.phones.ifEmpty { null },
                emails = b.emails.ifEmpty { null },
                accountName = agg.preferredAccountName,
                accountType = agg.preferredAccountType,
                readOnly = agg.sawAny && agg.allReadOnly,
                etag = agg.preferredEtag
            )
        }
    }

    suspend fun createContact(dto: ContactDto): ContactDto = withContext(Dispatchers.IO) {
        if (isReadOnlyAccount(dto.accountType))
            throw ReadOnlyAccountException("Cannot create contact in read-only account: ${dto.accountType}")

        val ops = arrayListOf<ContentProviderOperation>()
        ops.add(
            ContentProviderOperation.newInsert(ContactsContract.RawContacts.CONTENT_URI)
                .withValue(ContactsContract.RawContacts.ACCOUNT_TYPE, dto.accountType)
                .withValue(ContactsContract.RawContacts.ACCOUNT_NAME, dto.accountName)
                .withYieldAllowed(true)
                .build()
        )
        ops.add(
            ContentProviderOperation.newInsert(ContactsContract.Data.CONTENT_URI)
                .withValueBackReference(ContactsContract.Data.RAW_CONTACT_ID, 0)
                .withValue(ContactsContract.Data.MIMETYPE, CommonDataKinds.StructuredName.CONTENT_ITEM_TYPE)
                .withValue(CommonDataKinds.StructuredName.GIVEN_NAME, dto.firstName)
                .withValue(CommonDataKinds.StructuredName.MIDDLE_NAME, dto.middleName)
                .withValue(CommonDataKinds.StructuredName.FAMILY_NAME, dto.lastName)
                .withValue(CommonDataKinds.StructuredName.PREFIX, dto.prefix)
                .withValue(CommonDataKinds.StructuredName.SUFFIX, dto.suffix)
                .withYieldAllowed(true)
                .build()
        )
        dto.phones?.forEach { phone ->
            ops.add(
                ContentProviderOperation.newInsert(ContactsContract.Data.CONTENT_URI)
                    .withValueBackReference(ContactsContract.Data.RAW_CONTACT_ID, 0)
                    .withValue(ContactsContract.Data.MIMETYPE, CommonDataKinds.Phone.CONTENT_ITEM_TYPE)
                    .withValue(CommonDataKinds.Phone.NUMBER, phone.value)
                    .withValue(CommonDataKinds.Phone.TYPE, phone.type.toIntOrNull() ?: 2)
                    .withYieldAllowed(true)
                    .build()
            )
        }
        dto.emails?.forEach { email ->
            ops.add(
                ContentProviderOperation.newInsert(ContactsContract.Data.CONTENT_URI)
                    .withValueBackReference(ContactsContract.Data.RAW_CONTACT_ID, 0)
                    .withValue(ContactsContract.Data.MIMETYPE, CommonDataKinds.Email.CONTENT_ITEM_TYPE)
                    .withValue(CommonDataKinds.Email.ADDRESS, email.value)
                    .withValue(CommonDataKinds.Email.TYPE, email.type.toIntOrNull() ?: 1)
                    .withYieldAllowed(true)
                    .build()
            )
        }
        if (!dto.organization.isNullOrBlank() || !dto.title.isNullOrBlank()) {
            ops.add(
                ContentProviderOperation.newInsert(ContactsContract.Data.CONTENT_URI)
                    .withValueBackReference(ContactsContract.Data.RAW_CONTACT_ID, 0)
                    .withValue(ContactsContract.Data.MIMETYPE, CommonDataKinds.Organization.CONTENT_ITEM_TYPE)
                    .withValue(CommonDataKinds.Organization.COMPANY, dto.organization)
                    .withValue(CommonDataKinds.Organization.TITLE, dto.title)
                    .withYieldAllowed(true)
                    .build()
            )
        }

        val results = contentResolver.applyBatch(ContactsContract.AUTHORITY, ops)
        invalidateCache()
        val rawContactId = results[0].uri?.lastPathSegment
        if (rawContactId != null) {
            val cursor = contentResolver.query(
                ContactsContract.RawContacts.CONTENT_URI,
                arrayOf(ContactsContract.RawContacts.CONTACT_ID),
                "${ContactsContract.RawContacts._ID} = ?",
                arrayOf(rawContactId), null
            )
            cursor?.use {
                if (it.moveToFirst()) {
                    val contactId = it.getString(it.getColumnIndexOrThrow(ContactsContract.RawContacts.CONTACT_ID))
                    return@withContext getContactDetail(contactId) ?: dto.copy(id = contactId)
                }
            }
        }
        dto
    }

    suspend fun updateContact(dto: ContactDto): ContactDto = withContext(Dispatchers.IO) {
        if (dto.id == null) return@withContext createContact(dto)
        val rawContactId = getWritableRawContactId(dto.id!!)
            ?: throw ReadOnlyAccountException("Cannot update contact in read-only account.")

        val ops = arrayListOf<ContentProviderOperation>()

        ops.add(
            ContentProviderOperation.newDelete(ContactsContract.Data.CONTENT_URI)
                .withSelection(
                    "${ContactsContract.Data.RAW_CONTACT_ID} = ? AND ${ContactsContract.Data.MIMETYPE} IN (?,?,?,?)",
                    arrayOf(
                        rawContactId,
                        CommonDataKinds.StructuredName.CONTENT_ITEM_TYPE,
                        CommonDataKinds.Phone.CONTENT_ITEM_TYPE,
                        CommonDataKinds.Email.CONTENT_ITEM_TYPE,
                        CommonDataKinds.Organization.CONTENT_ITEM_TYPE
                    )
                )
                .withYieldAllowed(true)
                .build()
        )

        ops.add(
            ContentProviderOperation.newInsert(ContactsContract.Data.CONTENT_URI)
                .withValue(ContactsContract.Data.RAW_CONTACT_ID, rawContactId)
                .withValue(ContactsContract.Data.MIMETYPE, CommonDataKinds.StructuredName.CONTENT_ITEM_TYPE)
                .withValue(CommonDataKinds.StructuredName.GIVEN_NAME, dto.firstName)
                .withValue(CommonDataKinds.StructuredName.MIDDLE_NAME, dto.middleName)
                .withValue(CommonDataKinds.StructuredName.FAMILY_NAME, dto.lastName)
                .withValue(CommonDataKinds.StructuredName.PREFIX, dto.prefix)
                .withValue(CommonDataKinds.StructuredName.SUFFIX, dto.suffix)
                .withYieldAllowed(true)
                .build()
        )

        dto.phones?.forEach { phone ->
            ops.add(
                ContentProviderOperation.newInsert(ContactsContract.Data.CONTENT_URI)
                    .withValue(ContactsContract.Data.RAW_CONTACT_ID, rawContactId)
                    .withValue(ContactsContract.Data.MIMETYPE, CommonDataKinds.Phone.CONTENT_ITEM_TYPE)
                    .withValue(CommonDataKinds.Phone.NUMBER, phone.value)
                    .withValue(CommonDataKinds.Phone.TYPE, phone.type.toIntOrNull() ?: 2)
                    .withYieldAllowed(true)
                    .build()
            )
        }

        dto.emails?.forEach { email ->
            ops.add(
                ContentProviderOperation.newInsert(ContactsContract.Data.CONTENT_URI)
                    .withValue(ContactsContract.Data.RAW_CONTACT_ID, rawContactId)
                    .withValue(ContactsContract.Data.MIMETYPE, CommonDataKinds.Email.CONTENT_ITEM_TYPE)
                    .withValue(CommonDataKinds.Email.ADDRESS, email.value)
                    .withValue(CommonDataKinds.Email.TYPE, email.type.toIntOrNull() ?: 1)
                    .withYieldAllowed(true)
                    .build()
            )
        }

        if (!dto.organization.isNullOrBlank() || !dto.title.isNullOrBlank()) {
            ops.add(
                ContentProviderOperation.newInsert(ContactsContract.Data.CONTENT_URI)
                    .withValue(ContactsContract.Data.RAW_CONTACT_ID, rawContactId)
                    .withValue(ContactsContract.Data.MIMETYPE, CommonDataKinds.Organization.CONTENT_ITEM_TYPE)
                    .withValue(CommonDataKinds.Organization.COMPANY, dto.organization)
                    .withValue(CommonDataKinds.Organization.TITLE, dto.title)
                    .withYieldAllowed(true)
                    .build()
            )
        }

        contentResolver.applyBatch(ContactsContract.AUTHORITY, ops)
        invalidateCache()
        Log.d(TAG, "Updated contact ${dto.id}")
        getContactDetail(dto.id!!) ?: dto
    }

    suspend fun deleteContact(contactId: String): Boolean = withContext(Dispatchers.IO) {
        val rawContactId = getWritableRawContactId(contactId)
            ?: throw ReadOnlyAccountException("Cannot delete contact from read-only account.")
        val uri = ContactsContract.RawContacts.CONTENT_URI.buildUpon().appendPath(rawContactId).build()
        val deleted = contentResolver.delete(uri, null, null) > 0
        if (deleted) invalidateCache()
        deleted
    }

    suspend fun getContactPhoto(contactId: String): ByteArray? = withContext(Dispatchers.IO) {
        try {
            val contactUri = Uri.withAppendedPath(
                ContactsContract.Contacts.CONTENT_URI, contactId
            )
            val photoUri = Uri.withAppendedPath(
                contactUri, ContactsContract.Contacts.Photo.CONTENT_DIRECTORY
            )
            contentResolver.openInputStream(photoUri)?.use { it.readBytes() }
        } catch (e: Exception) {
            Log.w(TAG, "No photo for contact $contactId: ${e.message}")
            null
        }
    }

    suspend fun setContactPhoto(contactId: String, photoBytes: ByteArray): Boolean = withContext(Dispatchers.IO) {
        try {
            val rawContactId = getWritableRawContactId(contactId)
                ?: throw ReadOnlyAccountException("Cannot set photo on read-only contact.")

            val photoUri = Uri.withAppendedPath(
                ContentUris.withAppendedId(ContactsContract.RawContacts.CONTENT_URI, rawContactId.toLong()),
                ContactsContract.RawContacts.DisplayPhoto.CONTENT_DIRECTORY
            )
            contentResolver.openAssetFileDescriptor(photoUri, "rw")?.use { fd ->
                fd.createOutputStream().use { it.write(photoBytes) }
            }
            true
        } catch (e: ReadOnlyAccountException) {
            throw e
        } catch (e: Exception) {
            Log.e(TAG, "Failed to set photo for contact $contactId: ${e.message}")
            false
        }
    }

    suspend fun getGroups(): List<GroupDto> = withContext(Dispatchers.IO) {
        val groups = mutableListOf<GroupDto>()
        // Base CONTENT_URI doesn't include the calculated count column.
        val cursor = contentResolver.query(
            ContactsContract.Groups.CONTENT_SUMMARY_URI,
            arrayOf(
                ContactsContract.Groups._ID,
                ContactsContract.Groups.TITLE,
                ContactsContract.Groups.ACCOUNT_NAME,
                ContactsContract.Groups.ACCOUNT_TYPE,
                ContactsContract.Groups.SUMMARY_COUNT
            ),
            "${ContactsContract.Groups.DELETED} = 0",
            null,
            ContactsContract.Groups.TITLE + " ASC"
        )
        cursor?.use {
            val idIdx = it.getColumnIndexOrThrow(ContactsContract.Groups._ID)
            val titleIdx = it.getColumnIndexOrThrow(ContactsContract.Groups.TITLE)
            val accountNameIdx = it.getColumnIndexOrThrow(ContactsContract.Groups.ACCOUNT_NAME)
            val accountTypeIdx = it.getColumnIndexOrThrow(ContactsContract.Groups.ACCOUNT_TYPE)
            val countIdx = it.getColumnIndex(ContactsContract.Groups.SUMMARY_COUNT)
            while (it.moveToNext()) {
                val title = it.getString(titleIdx) ?: continue
                if (title.isBlank()) continue
                groups.add(GroupDto(
                    id = it.getString(idIdx),
                    title = title,
                    accountName = it.getString(accountNameIdx),
                    accountType = it.getString(accountTypeIdx),
                    memberCount = if (countIdx >= 0) it.getInt(countIdx) else 0
                ))
            }
        }
        groups
    }

    suspend fun getContactsByGroup(groupId: String): List<ContactDto> = withContext(Dispatchers.IO) {
        val contactIds = linkedSetOf<String>()
        contentResolver.query(
            ContactsContract.Data.CONTENT_URI,
            arrayOf(ContactsContract.Data.CONTACT_ID),
            "${ContactsContract.Data.MIMETYPE} = ? AND " +
                "${ContactsContract.CommonDataKinds.GroupMembership.GROUP_ROW_ID} = ?",
            arrayOf(ContactsContract.CommonDataKinds.GroupMembership.CONTENT_ITEM_TYPE, groupId),
            null
        )?.use {
            val idIdx = it.getColumnIndexOrThrow(ContactsContract.Data.CONTACT_ID)
            while (it.moveToNext()) contactIds.add(it.getString(idIdx))
        }

        if (contactIds.isEmpty()) return@withContext emptyList()

        val placeholders = contactIds.joinToString(",") { "?" }
        val selArgs = contactIds.toTypedArray()

        data class ContactBuilder(
            var firstName: String? = null,
            var middleName: String? = null,
            var lastName: String? = null,
            var prefix: String? = null,
            var suffix: String? = null,
            var fullName: String = "",
            var organization: String? = null,
            var title: String? = null,
            val phones: MutableList<PhoneDto> = mutableListOf(),
            val emails: MutableList<EmailDto> = mutableListOf()
        )
        val builderMap = linkedMapOf<String, ContactBuilder>()
        contactIds.forEach { builderMap[it] = ContactBuilder() }

        contentResolver.query(
            ContactsContract.Data.CONTENT_URI,
            arrayOf(
                ContactsContract.Data.CONTACT_ID,
                ContactsContract.Data.MIMETYPE,
                ContactsContract.CommonDataKinds.StructuredName.GIVEN_NAME,
                ContactsContract.CommonDataKinds.StructuredName.MIDDLE_NAME,
                ContactsContract.CommonDataKinds.StructuredName.FAMILY_NAME,
                ContactsContract.CommonDataKinds.StructuredName.PREFIX,
                ContactsContract.CommonDataKinds.StructuredName.SUFFIX,
                ContactsContract.CommonDataKinds.StructuredName.DISPLAY_NAME,
                ContactsContract.CommonDataKinds.Phone.NUMBER,
                ContactsContract.CommonDataKinds.Phone.TYPE,
                ContactsContract.CommonDataKinds.Email.ADDRESS,
                ContactsContract.CommonDataKinds.Email.TYPE,
                ContactsContract.CommonDataKinds.Organization.COMPANY,
                ContactsContract.CommonDataKinds.Organization.TITLE
            ),
            "${ContactsContract.Data.CONTACT_ID} IN ($placeholders)",
            selArgs,
            null
        )?.use { cursor ->
            val contactIdIdx = cursor.getColumnIndexOrThrow(ContactsContract.Data.CONTACT_ID)
            val mimeIdx      = cursor.getColumnIndexOrThrow(ContactsContract.Data.MIMETYPE)

            while (cursor.moveToNext()) {
                val cid = cursor.getString(contactIdIdx) ?: continue
                val b = builderMap[cid] ?: continue
                val mime = cursor.getString(mimeIdx) ?: continue

                when (mime) {
                    ContactsContract.CommonDataKinds.StructuredName.CONTENT_ITEM_TYPE -> {
                        b.firstName  = cursor.getString(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.StructuredName.GIVEN_NAME))
                        b.middleName = cursor.getString(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.StructuredName.MIDDLE_NAME))
                        b.lastName   = cursor.getString(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.StructuredName.FAMILY_NAME))
                        b.prefix     = cursor.getString(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.StructuredName.PREFIX))
                        b.suffix     = cursor.getString(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.StructuredName.SUFFIX))
                        val dn = cursor.getString(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.StructuredName.DISPLAY_NAME))
                        if (!dn.isNullOrBlank()) b.fullName = dn
                    }
                    ContactsContract.CommonDataKinds.Phone.CONTENT_ITEM_TYPE -> {
                        val number = cursor.getString(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.Phone.NUMBER))
                        val type   = cursor.getInt(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.Phone.TYPE)).toString()
                        if (!number.isNullOrBlank()) {
                            val norm = number.filter { it.isDigit() || it == '+' }
                            if (b.phones.none { it.value.filter { c -> c.isDigit() || c == '+' } == norm }) {
                                b.phones.add(PhoneDto(value = number, type = type))
                            }
                        }
                    }
                    ContactsContract.CommonDataKinds.Email.CONTENT_ITEM_TYPE -> {
                        val address = cursor.getString(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.Email.ADDRESS))
                        val type    = cursor.getInt(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.Email.TYPE)).toString()
                        if (!address.isNullOrBlank()) {
                            if (b.emails.none { it.value.equals(address, ignoreCase = true) }) {
                                b.emails.add(EmailDto(value = address, type = type))
                            }
                        }
                    }
                    ContactsContract.CommonDataKinds.Organization.CONTENT_ITEM_TYPE -> {
                        b.organization = cursor.getString(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.Organization.COMPANY))
                        b.title        = cursor.getString(cursor.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.Organization.TITLE))
                    }
                }
            }
        }

        builderMap.entries.map { (cid, b) ->
            ContactDto(
                id = cid,
                firstName = b.firstName,
                middleName = b.middleName,
                lastName = b.lastName,
                prefix = b.prefix,
                suffix = b.suffix,
                fullName = b.fullName,
                organization = b.organization,
                title = b.title,
                phones = b.phones.ifEmpty { null },
                emails = b.emails.ifEmpty { null }
            )
        }
    }

    suspend fun getAccounts(): List<String> = withContext(Dispatchers.IO) {
        val accounts = mutableSetOf<String>()
        val cursor = contentResolver.query(
            ContactsContract.RawContacts.CONTENT_URI,
            arrayOf(ContactsContract.RawContacts.ACCOUNT_NAME, ContactsContract.RawContacts.ACCOUNT_TYPE),
            null, null, null
        )
        cursor?.use {
            while (it.moveToNext()) {
                val type = it.getString(it.getColumnIndexOrThrow(ContactsContract.RawContacts.ACCOUNT_TYPE))
                if (!type.isNullOrBlank()) accounts.add(type)
            }
        }
        accounts.toList().sorted()
    }

    suspend fun importVcf(inputStream: InputStream, accountName: String?, accountType: String?): ImportResult = withContext(Dispatchers.IO) {
        val handler = VcfHandler()
        val contacts = handler.parseVcf(inputStream)
        var imported = 0
        var failed = 0
        var skipped = 0

        fun String?.normalise() = this?.lowercase()?.filter { it.isLetterOrDigit() } ?: ""

        val existingKeys = hashSetOf<String>()
        contentResolver.query(
            ContactsContract.Contacts.CONTENT_URI,
            arrayOf(ContactsContract.Contacts.DISPLAY_NAME_PRIMARY),
            null, null, null
        )?.use { cursor ->
            val nameIdx = cursor.getColumnIndexOrThrow(ContactsContract.Contacts.DISPLAY_NAME_PRIMARY)
            while (cursor.moveToNext()) {
                val key = cursor.getString(nameIdx).normalise()
                if (key.isNotEmpty()) existingKeys.add(key)
            }
        }

        contacts.forEach { dto ->
            try {
                val key = dto.fullName.normalise()
                if (key.isNotEmpty() && existingKeys.contains(key)) {
                    Log.d(TAG, "Skipping duplicate contact: ${dto.fullName}")
                    skipped++
                    return@forEach
                }
                createContact(dto.copy(accountName = accountName, accountType = accountType))
                existingKeys.add(key)
                imported++
            } catch (e: Exception) {
                Log.w(TAG, "Failed to import contact ${dto.fullName}: ${e.message}")
                failed++
            }
        }
        ImportResult(total = contacts.size, imported = imported, failed = failed, skipped = skipped)
    }

    suspend fun exportVcf(contactIds: List<String>? = null): String = withContext(Dispatchers.IO) {
        val sb = StringBuilder()
        exportVcfTo(sbWriter = { chunk -> sb.append(chunk) }, contactIds = contactIds)
        sb.toString()
    }

    suspend fun exportVcfTo(output: OutputStream, contactIds: List<String>? = null) = withContext(Dispatchers.IO) {
        exportVcfTo(sbWriter = { chunk -> output.write(chunk.toByteArray(Charsets.UTF_8)) }, contactIds = contactIds)
    }

    private suspend fun exportVcfTo(sbWriter: (String) -> Unit, contactIds: List<String>? = null) {
        val handler = VcfHandler()

        if (contactIds != null) {
            val details = contactIds.mapNotNull { getContactDetail(it) }
            sbWriter(handler.generateVcf(details))
            return
        }

        var page = 1
        while (true) {
            val safePageSize = DEFAULT_PAGE_SIZE
            val offset = (page - 1) * safePageSize
            val queryArgs = Bundle().apply {
                putStringArray(
                    ContentResolver.QUERY_ARG_SORT_COLUMNS,
                    arrayOf(ContactsContract.Contacts.DISPLAY_NAME_PRIMARY)
                )
                putInt(ContentResolver.QUERY_ARG_SORT_DIRECTION, ContentResolver.QUERY_SORT_DIRECTION_ASCENDING)
                putInt(ContentResolver.QUERY_ARG_LIMIT, safePageSize)
                putInt(ContentResolver.QUERY_ARG_OFFSET, offset)
            }
            val ids = mutableListOf<String>()
            contentResolver.query(
                ContactsContract.Contacts.CONTENT_URI,
                arrayOf(ContactsContract.Contacts._ID),
                queryArgs,
                null
            )?.use { cursor ->
                val idIdx = cursor.getColumnIndexOrThrow(ContactsContract.Contacts._ID)
                while (cursor.moveToNext()) ids.add(cursor.getString(idIdx))
            }

            if (ids.isEmpty()) break

            val pageDetails = ids.mapNotNull { getContactDetail(it) }
            sbWriter(handler.generateVcf(pageDetails))

            if (ids.size < safePageSize) break
            page++
        }
    }

    suspend fun getAllContactIds(): List<String> = withContext(Dispatchers.IO) {
        val ids = mutableListOf<String>()
        contentResolver.query(
            ContactsContract.Contacts.CONTENT_URI,
            arrayOf(ContactsContract.Contacts._ID),
            null, null, null
        )?.use { cursor ->
            val idIdx = cursor.getColumnIndexOrThrow(ContactsContract.Contacts._ID)
            while (cursor.moveToNext()) ids.add(cursor.getString(idIdx))
        }
        ids
    }

    suspend fun exportAllContactsAsVcf(): String = exportVcf(contactIds = null)

    suspend fun importContactsFromVcf(vcfContent: String): ImportResult =
        importVcf(vcfContent.byteInputStream(Charsets.UTF_8), accountName = null, accountType = null)

    private fun isReadOnlyAccount(accountType: String?) =
        accountType != null && READ_ONLY_ACCOUNT_TYPES.contains(accountType)

    private fun getWritableRawContactId(contactId: String): String? {
        val cursor = contentResolver.query(
            ContactsContract.RawContacts.CONTENT_URI,
            arrayOf(ContactsContract.RawContacts._ID, ContactsContract.RawContacts.ACCOUNT_TYPE),
            "${ContactsContract.RawContacts.CONTACT_ID} = ?",
            arrayOf(contactId), null
        )
        var firstRawId: String? = null
        var firstAccountType: String? = null
        cursor?.use {
            val idIdx = it.getColumnIndexOrThrow(ContactsContract.RawContacts._ID)
            val typeIdx = it.getColumnIndexOrThrow(ContactsContract.RawContacts.ACCOUNT_TYPE)
            while (it.moveToNext()) {
                val rawId = it.getString(idIdx)
                val accountType = it.getString(typeIdx)
                if (firstRawId == null) { firstRawId = rawId; firstAccountType = accountType }
                if (!isReadOnlyAccount(accountType)) return rawId
            }
        }
        if (firstRawId != null && isReadOnlyAccount(firstAccountType)) return null
        return firstRawId
    }

    suspend fun getDuplicateGroups(): List<com.aeliavision.androiddeck.feature.contacts.model.DuplicateGroup> = withContext(Dispatchers.IO) {
        val allContacts = getContacts(pageSize = 10000)
        val nameGroups = allContacts.groupBy { it.fullName.lowercase().trim() }
            .filter { it.value.size > 1 && it.key.isNotBlank() }
            .map {
                com.aeliavision.androiddeck.feature.contacts.model.DuplicateGroup(
                    type = "name",
                    value = it.key,
                    contactIds = it.value.mapNotNull { c -> c.id }
                )
            }
        
        nameGroups
    }

    suspend fun mergeContacts(targetId: String, sourceIds: List<String>) = withContext(Dispatchers.IO) {
        val target = getContactDetail(targetId) ?: return@withContext
        val sources = sourceIds.mapNotNull { getContactDetail(it) }
        
        val allPhones = (target.phones ?: emptyList()) + sources.flatMap { it.phones ?: emptyList() }
        val allEmails = (target.emails ?: emptyList()) + sources.flatMap { it.emails ?: emptyList() }
        
        val uniquePhones = allPhones.distinctBy { it.value.filter { c -> c.isDigit() } }
        val uniqueEmails = allEmails.distinctBy { it.value.lowercase() }
        
        val merged = target.copy(
            phones = uniquePhones.ifEmpty { null },
            emails = uniqueEmails.ifEmpty { null }
        )
        
        updateContact(merged)
        sourceIds.forEach { deleteContact(it) }
        invalidateCache()
    }
}
