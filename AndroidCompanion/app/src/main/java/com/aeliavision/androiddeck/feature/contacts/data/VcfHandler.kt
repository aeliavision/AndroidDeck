package com.aeliavision.androiddeck.feature.contacts.data

import com.aeliavision.androiddeck.feature.contacts.model.ContactDto
import com.aeliavision.androiddeck.feature.contacts.model.PhoneDto
import com.aeliavision.androiddeck.feature.contacts.model.EmailDto
import java.io.InputStream
import java.nio.charset.Charset

/**
 * Handles VCF (vCard) parsing and generation.
 * Supports vCard 2.1 and 3.0 formats.
 */
class VcfHandler {
    // Parsing

    /**
     * Reads a .vcf stream and returns a list of [ContactDto] objects.
     * Handles:
     *  - vCard 2.1 and 3.0
     *  - BEGIN:VCARD / END:VCARD blocks
     *  - Line folding (continuation lines starting with space or tab)
     *  - ENCODING=QUOTED-PRINTABLE and CHARSET=UTF-8 parameters
     *  - N, FN, TEL, EMAIL, ORG, TITLE properties
     */
    fun parseVcf(inputStream: InputStream): List<ContactDto> {
        val contacts = mutableListOf<ContactDto>()

        // Read all raw bytes once; we'll decode line by line respecting CHARSET params
        val rawLines = inputStream.bufferedReader(Charsets.UTF_8).readLines()

        // Unfold lines: a line that starts with SPACE or TAB is a continuation of the previous
        val unfoldedLines = mutableListOf<String>()
        for (raw in rawLines) {
            if ((raw.startsWith(" ") || raw.startsWith("\t")) && unfoldedLines.isNotEmpty()) {
                unfoldedLines[unfoldedLines.lastIndex] += raw.substring(1)
            } else {
                unfoldedLines.add(raw)
            }
        }

        var inVcard = false
        var currentLines = mutableListOf<String>()

        for (line in unfoldedLines) {
            val upper = line.trim().uppercase()
            when {
                upper == "BEGIN:VCARD" -> {
                    inVcard = true
                    currentLines = mutableListOf()
                }
                upper == "END:VCARD" -> {
                    if (inVcard) {
                        parseVcardBlock(currentLines)?.let { contacts.add(it) }
                    }
                    inVcard = false
                }
                inVcard -> currentLines.add(line)
            }
        }

        return contacts
    }

    /** Parses a single vCard block (lines between BEGIN:VCARD and END:VCARD). */
    private fun parseVcardBlock(lines: List<String>): ContactDto? {
        // accumulate parsed vCard properties, then construct a single immutable ContactDto
        // at the end via the primary constructor instead of mutating fields post-construction.
        var fullName: String = ""
        var firstName: String? = null
        var middleName: String? = null
        var lastName: String? = null
        var prefix: String? = null
        var suffix: String? = null
        var organization: String? = null
        var title: String? = null
        val phones = mutableListOf<PhoneDto>()
        val emails = mutableListOf<EmailDto>()

        for (line in lines) {
            if (line.isBlank()) continue

            // Split property name+params from value at the first unescaped ':'
            val colonIdx = line.indexOf(':')
            if (colonIdx < 0) continue

            val propPart = line.substring(0, colonIdx)   // e.g. "TEL;TYPE=CELL;ENCODING=QUOTED-PRINTABLE"
            var value    = line.substring(colonIdx + 1)  // everything after ':'

            // Parse property name and parameters
            val segments = propPart.split(";")
            val propName = segments[0].trim().uppercase()
            val params   = segments.drop(1).map { it.trim().uppercase() }

            // Determine charset (default UTF-8)
            val charsetParam = params.firstOrNull { it.startsWith("CHARSET=") }
                ?.removePrefix("CHARSET=") ?: "UTF-8"
            val charset = runCatching { Charset.forName(charsetParam) }.getOrElse { Charsets.UTF_8 }

            // Decode quoted-printable if needed
            val isQP = params.any { it == "ENCODING=QUOTED-PRINTABLE" || it == "QUOTED-PRINTABLE" }
            if (isQP) {
                value = decodeQuotedPrintable(value, charset)
            }

            when (propName) {
                "FN" -> fullName = value.trim()

                "N" -> {
                    // N:last;first;middle;prefix;suffix
                    val parts = splitUnescaped(value, ';')
                    lastName   = parts.getOrNull(0)?.unescape()?.takeIf { it.isNotBlank() }
                    firstName  = parts.getOrNull(1)?.unescape()?.takeIf { it.isNotBlank() }
                    middleName = parts.getOrNull(2)?.unescape()?.takeIf { it.isNotBlank() }
                    prefix     = parts.getOrNull(3)?.unescape()?.takeIf { it.isNotBlank() }
                    suffix     = parts.getOrNull(4)?.unescape()?.takeIf { it.isNotBlank() }
                }

                "TEL" -> {
                    // AND-C03 FIX: collect ALL TYPE= params and join them, so that both
                    // "TEL;TYPE=CELL;TYPE=VOICE:+1234" and "TEL;TYPE=CELL,VOICE:+1234"
                    // are handled correctly. Previously only the first TYPE= param was used.
                    val number = value.trim()
                    if (number.isNotBlank()) {
                        val typeParam = params
                            .filter { it.startsWith("TYPE=") }
                            .joinToString(",") { it.removePrefix("TYPE=") }
                            .let { combined ->
                                // Also expand comma-separated values within a single TYPE=
                                combined.split(",").joinToString(",")
                            }
                        val phoneType = vcfPhoneTypeTo(typeParam)
                        phones.add(PhoneDto(value = number, type = phoneType))
                    }
                }

                "EMAIL" -> {
                    val address = value.trim()
                    if (address.isNotBlank()) {
                        val typeParam = params.firstOrNull { it.startsWith("TYPE=") }
                            ?.removePrefix("TYPE=") ?: ""
                        val emailType = vcfEmailTypeTo(typeParam)
                        emails.add(EmailDto(value = address, type = emailType))
                    }
                }
                "ORG" -> {
                    organization = value.unescape().trim().takeIf { it.isNotBlank() }
                    if (fullName.isBlank() && !organization.isNullOrBlank()) {
                        fullName = organization!!
                    }
                }

                "TITLE" -> title = value.unescape().trim().takeIf { it.isNotBlank() }
            }
        }
        // Previously a contact with N but no FN line would still produce a blank fullName
        // and get dropped if it had no phones/emails — losing valid org-only contacts.
        if (fullName.isBlank()) {
            fullName = listOfNotNull(prefix, firstName, middleName, lastName, suffix)
                .joinToString(" ").trim()
        }
        // Only skip contacts that have absolutely no identifying information at all.
        if (fullName.isBlank() && organization.isNullOrBlank() &&
            phones.isEmpty() && emails.isEmpty()) return null

        return ContactDto(
            fullName = fullName,
            firstName = firstName,
            middleName = middleName,
            lastName = lastName,
            prefix = prefix,
            suffix = suffix,
            organization = organization,
            title = title,
            phones = phones.ifEmpty { null },
            emails = emails.ifEmpty { null }
        )
    }
    // Generation

    /**
     * Generates a vCard 3.0 string from a list of [ContactDto].
     * Each value has semicolons and backslashes escaped per RFC 6350.
     */
    fun generateVcf(contacts: List<ContactDto>): String {
        val sb = StringBuilder()
        for (contact in contacts) {
            sb.append("BEGIN:VCARD\r\n")
            sb.append("VERSION:3.0\r\n")

            // N: last;first;middle;prefix;suffix
            val last   = contact.lastName?.escape()   ?: ""
            val first  = contact.firstName?.escape()  ?: ""
            val middle = contact.middleName?.escape() ?: ""
            val prefix = contact.prefix?.escape()     ?: ""
            val suffix = contact.suffix?.escape()     ?: ""
            sb.append("N:$last;$first;$middle;$prefix;$suffix\r\n")

            // FN
            val fn = contact.fullName.ifBlank {
                listOf(contact.prefix, contact.firstName, contact.middleName,
                    contact.lastName, contact.suffix)
                    .filterNotNull().filter { it.isNotBlank() }.joinToString(" ")
            }
            sb.append("FN:${fn.escape()}\r\n")

            // TEL
            contact.phones?.forEach { phone ->
                val typeLabel = phoneTypeToVcf(phone.type)
                sb.append("TEL;TYPE=$typeLabel:${phone.value}\r\n")
            }

            // EMAIL
            contact.emails?.forEach { email ->
                val typeLabel = emailTypeToVcf(email.type)
                sb.append("EMAIL;TYPE=$typeLabel:${email.value}\r\n")
            }

            // ORG
            if (!contact.organization.isNullOrBlank()) {
                sb.append("ORG:${contact.organization!!.escape()}\r\n")
            }

            // TITLE
            if (!contact.title.isNullOrBlank()) {
                sb.append("TITLE:${contact.title!!.escape()}\r\n")
            }

            sb.append("END:VCARD\r\n")
        }
        return sb.toString()
    }
    // Helpers

    /** Splits [input] by [delimiter] but not when preceded by a backslash. */
    private fun splitUnescaped(input: String, delimiter: Char): List<String> {
        val parts = mutableListOf<String>()
        val current = StringBuilder()
        var i = 0
        while (i < input.length) {
            val ch = input[i]
            if (ch == '\\' && i + 1 < input.length) {
                current.append(ch)
                current.append(input[i + 1])
                i += 2
            } else if (ch == delimiter) {
                parts.add(current.toString())
                current.clear()
                i++
            } else {
                current.append(ch)
                i++
            }
        }
        parts.add(current.toString())
        return parts
    }

    /** Un-escapes vCard text sequences: \; \, \\ \n \N */
    private fun String.unescape(): String =
        this.replace("\\n", "\n")
            .replace("\\N", "\n")
            .replace("\\,", ",")
            .replace("\\;", ";")
            .replace("\\\\", "\\")

    /** Escapes semicolons and backslashes for vCard 3.0 values. */
    private fun String.escape(): String =
        this.replace("\\", "\\\\")
            .replace(";", "\\;")
            .replace(",", "\\,")
            .replace("\n", "\\n")

    /**
     * Decodes a quoted-printable encoded string, respecting soft line breaks
     * (trailing `=` means the encoded line continues) and the given charset.
     */
    private fun decodeQuotedPrintable(input: String, charset: Charset): String {
        val bytes = mutableListOf<Byte>()
        var i = 0
        val s = input.replace("=\r\n", "").replace("=\n", "") // soft line breaks
        while (i < s.length) {
            if (s[i] == '=' && i + 2 < s.length) {
                val hex = s.substring(i + 1, i + 3)
                val byte = hex.toIntOrNull(16)
                if (byte != null) {
                    bytes.add(byte.toByte())
                    i += 3
                } else {
                    bytes.add(s[i].code.toByte())
                    i++
                }
            } else {
                bytes.add(s[i].code.toByte())
                i++
            }
        }
        return String(bytes.toByteArray(), charset)
    }

    // --- Phone type mapping ---------------------------------------------------

    /** Maps a vCard TYPE param string to Android CommonDataKinds.Phone type String. */
    private fun vcfPhoneTypeTo(type: String): String {
        // SER-01 FIX: return String instead of Int to match PhoneDto.type = String.
        return when {
            type.contains("CELL", ignoreCase = true)   -> "2"  // TYPE_MOBILE
            type.contains("MOBILE", ignoreCase = true) -> "2"
            type.contains("HOME", ignoreCase = true)   -> "1"  // TYPE_HOME
            type.contains("WORK", ignoreCase = true)   -> "3"  // TYPE_WORK
            type.contains("FAX", ignoreCase = true)    -> "5"  // TYPE_FAX_WORK (rough match)
            type.contains("PAGER", ignoreCase = true)  -> "6"  // TYPE_PAGER
            type.contains("MAIN", ignoreCase = true)   -> "12" // TYPE_MAIN
            else                                        -> "0"  // TYPE_CUSTOM
        }
    }

    /** Maps Android CommonDataKinds.Phone type String to a vCard TYPE label. */
    private fun phoneTypeToVcf(type: String): String = when (type.toIntOrNull()) {
        1  -> "HOME"
        2  -> "CELL"
        3  -> "WORK"
        4  -> "FAX,WORK"
        5  -> "FAX,HOME"
        6  -> "PAGER"
        7  -> "OTHER"
        12 -> "MAIN"
        else -> "VOICE"
    }

    // --- Email type mapping ---------------------------------------------------

    /** Maps a vCard TYPE param string to Android CommonDataKinds.Email type String. */
    private fun vcfEmailTypeTo(type: String): String {
        // SER-01 FIX: return String instead of Int to match EmailDto.type = String.
        return when {
            type.contains("HOME", ignoreCase = true)  -> "1"  // TYPE_HOME
            type.contains("WORK", ignoreCase = true)  -> "2"  // TYPE_WORK
            type.contains("OTHER", ignoreCase = true) -> "3"  // TYPE_OTHER
            else                                       -> "1"  // default HOME
        }
    }

    /** Maps Android CommonDataKinds.Email type String to a vCard TYPE label. */
    private fun emailTypeToVcf(type: String): String = when (type.toIntOrNull()) {
        1  -> "HOME"
        2  -> "WORK"
        3  -> "OTHER"
        4  -> "MOBILE"
        else -> "INTERNET"
    }
}
