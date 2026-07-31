package com.aeliavision.androiddeck.feature.backup.crypto

import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * Small HKDF-SHA256 helper (RFC 5869) used for backup key derivation.
 */
object Hkdf {
    private const val HKDF_MAC = "HmacSHA256"

    fun hkdfSha256(ikm: ByteArray, salt: ByteArray, info: ByteArray, length: Int): ByteArray {
        // Extract
        val mac = Mac.getInstance(HKDF_MAC)
        val effectiveSalt = if (salt.isEmpty()) ByteArray(32) else salt
        mac.init(SecretKeySpec(effectiveSalt, HKDF_MAC))
        val prk = mac.doFinal(ikm)

        // Expand
        val okm = ByteArray(length)
        var t = ByteArray(0)
        var offset = 0
        var counter = 1

        while (offset < length) {
            mac.init(SecretKeySpec(prk, HKDF_MAC))
            mac.update(t)
            mac.update(info)
            mac.update(counter.toByte())
            t = mac.doFinal()

            val copy = minOf(t.size, length - offset)
            System.arraycopy(t, 0, okm, offset, copy)
            offset += copy
            counter++
        }
        return okm
    }
}
