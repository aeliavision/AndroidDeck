package com.aeliavision.androiddeck.feature.server.service

import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.spec.ECGenParameterSpec
import java.security.spec.X509EncodedKeySpec
import java.util.Base64
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec
import org.junit.Assert.assertArrayEquals
import org.junit.Test

class EcdhHelperTest {
    @Test fun clientAndServerDeriveSameV3Secret() {
        val generator = KeyPairGenerator.getInstance("EC")
        generator.initialize(ECGenParameterSpec("secp256r1"))
        val client = generator.generateKeyPair()
        val sessionId = "session-123"

        val server = EcdhHelper.deriveSharedSecret(
            Base64.getEncoder().encodeToString(client.public.encoded),
            sessionId.toByteArray(),
            "AndroidDeck pairing v3".toByteArray()
        )

        val serverPublic = KeyFactory.getInstance("EC").generatePublic(
            X509EncodedKeySpec(Base64.getDecoder().decode(server.serverPublicKeyBase64)))
        val agreement = KeyAgreement.getInstance("ECDH")
        agreement.init(client.private)
        agreement.doPhase(serverPublic, true)
        val raw = agreement.generateSecret()
        val expected = hkdf(raw, sessionId.toByteArray(), "AndroidDeck pairing v3".toByteArray(), 32)
        raw.fill(0)

        assertArrayEquals(expected, server.derivedSecret)
    }

    private fun hkdf(ikm: ByteArray, salt: ByteArray, info: ByteArray, length: Int): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(salt, "HmacSHA256"))
        val prk = mac.doFinal(ikm)
        val output = ByteArray(length)
        var previous = ByteArray(0)
        var offset = 0
        var counter = 1
        while (offset < length) {
            mac.init(SecretKeySpec(prk, "HmacSHA256"))
            mac.update(previous); mac.update(info); mac.update(counter.toByte())
            previous = mac.doFinal()
            val count = minOf(previous.size, length - offset)
            previous.copyInto(output, offset, 0, count)
            offset += count; counter++
        }
        prk.fill(0); previous.fill(0)
        return output
    }
}
