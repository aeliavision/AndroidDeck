package com.aeliavision.androiddeck.feature.server.service

import android.content.Context
import android.content.SharedPreferences
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import android.util.Log
import androidx.core.content.edit
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 */
object KeystorePasswordProvider {

    private const val TAG = "KeystorePasswordProvider"
    private const val KEYSTORE_ALIAS = "vcfeditor_tls_pw_key"
    private const val ANDROID_KEYSTORE = "AndroidKeyStore"
    private const val TRANSFORMATION = "AES/GCM/NoPadding"
    private const val GCM_TAG_LENGTH = 128
    private const val PREFS_NAME = "vcfeditor_sec"
    private const val PREF_ENC_PW = "tls_pw_enc"

    @Synchronized
    fun getOrCreatePassword(context: Context): CharArray {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val existing = prefs.getString(PREF_ENC_PW, null)

        if (existing != null) {
            return try {
                decryptPassword(existing)
            } catch (e: Exception) {
                Log.w(TAG, "Failed to decrypt stored TLS password — regenerating: ${e.message}")
                generateAndStore(context, prefs)
            }
        }

        return generateAndStore(context, prefs)
    }

    private fun generateAndStore(
        context: Context,
        prefs: SharedPreferences
    ): CharArray {
        val raw = ByteArray(32).also { java.security.SecureRandom().nextBytes(it) }
        val password = bytesToChars(raw)
        val encrypted = encryptPassword(raw)
        prefs.edit { putString(PREF_ENC_PW, encrypted) }
        Log.i(TAG, "Generated and stored new TLS keystore password.")
        return password
    }

    private fun getOrCreateAesKey(): SecretKey {
        val ks = KeyStore.getInstance(ANDROID_KEYSTORE).also { it.load(null) }
        ks.getKey(KEYSTORE_ALIAS, null)?.let { return it as SecretKey }

        val spec = KeyGenParameterSpec.Builder(
            KEYSTORE_ALIAS,
            KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
        )
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
            .setUserAuthenticationRequired(false)
            .build()

        val kg = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        kg.init(spec)
        return kg.generateKey()
    }

    private fun encryptPassword(raw: ByteArray): String {
        val key = getOrCreateAesKey()
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, key)
        val iv = cipher.iv
        val ciphertext = cipher.doFinal(raw)
        val blob = iv + ciphertext
        return Base64.encodeToString(blob, Base64.NO_WRAP)
    }

    private fun decryptPassword(encoded: String): CharArray {
        val blob = Base64.decode(encoded, Base64.NO_WRAP)
        val iv = blob.copyOfRange(0, 12)
        val ciphertext = blob.copyOfRange(12, blob.size)
        val key = getOrCreateAesKey()
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(GCM_TAG_LENGTH, iv))
        val raw = cipher.doFinal(ciphertext)
        return bytesToChars(raw)
    }

    private fun bytesToChars(raw: ByteArray): CharArray =
        Base64.encodeToString(raw, Base64.NO_WRAP).toCharArray()
}
