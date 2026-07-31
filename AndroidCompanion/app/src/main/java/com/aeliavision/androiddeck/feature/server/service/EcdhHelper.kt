package com.aeliavision.androiddeck.feature.server.service

import java.util.Base64
import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.PublicKey
import java.security.SecureRandom
import java.security.spec.X509EncodedKeySpec
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 */
object EcdhHelper {

    private const val EC_ALGORITHM = "EC"
    private const val CURVE = "secp256r1"   // P-256 — universally supported
    private const val KA_ALGORITHM = "ECDH"
    private const val HKDF_MAC = "HmacSHA256"

    /** Result of a server-side ECDH operation. */
    data class EcdhResult(

        val serverPublicKeyBase64: String,

        val derivedSecret: ByteArray
    )

    /**
     * Given the desktop's Base64-encoded DER public key, generate a server keypair,
     * perform ECDH, and derive the HMAC secret via HKDF-SHA256.
     */
    fun deriveSharedSecret(clientPublicKeyBase64: String, salt: ByteArray, info: ByteArray = "AndroidDeck pairing v3".toByteArray()): EcdhResult {
        // Decode the client's public key.
        val clientKeyBytes = Base64.getDecoder().decode(clientPublicKeyBase64)
        val clientPublicKey: PublicKey = KeyFactory
            .getInstance(EC_ALGORITHM)
            .generatePublic(X509EncodedKeySpec(clientKeyBytes))

        // Generate server ephemeral keypair
        val kpg = KeyPairGenerator.getInstance(EC_ALGORITHM)
        kpg.initialize(java.security.spec.ECGenParameterSpec(CURVE), SecureRandom())
        val serverKeyPair = kpg.generateKeyPair()

        // ECDH: derive raw shared secret.
        val ka = KeyAgreement.getInstance(KA_ALGORITHM)
        ka.init(serverKeyPair.private)
        ka.doPhase(clientPublicKey, true)
        val rawSecret = ka.generateSecret()  // 32 bytes on P-256

        // HKDF-SHA256: derive a 32-byte HMAC session secret.
        val derived = hkdfSha256(rawSecret, salt, info = info, 32)
        rawSecret.fill(0)

        val serverPubDer = serverKeyPair.public.encoded
        return EcdhResult(
            serverPublicKeyBase64 = Base64.getEncoder().encodeToString(serverPubDer),
            derivedSecret = derived
        )
    }

    private fun hkdfSha256(ikm: ByteArray, salt: ByteArray, info: ByteArray, length: Int): ByteArray {
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
