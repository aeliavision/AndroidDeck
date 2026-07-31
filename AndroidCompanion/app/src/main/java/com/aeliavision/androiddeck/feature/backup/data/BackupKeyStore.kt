package com.aeliavision.androiddeck.feature.backup.data

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.first
import org.json.JSONObject
import java.security.KeyStore
import java.security.SecureRandom
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import javax.inject.Inject
import javax.inject.Singleton

private val Context.backupDataStore: DataStore<Preferences> by preferencesDataStore(
    name = "vcfeditor_backup_prefs"
)

/**
 *
 * Stores a random 32-byte backup seed encrypted using a hardware-backed AES key
 * from the Android Keystore. This makes backup archives decryptable across app
 * restarts and pairing/session expiry.
 */
@Singleton
class BackupKeyStore @Inject constructor(
    @ApplicationContext private val context: Context
) {
    private companion object {
        private const val ANDROID_KEYSTORE = "AndroidKeyStore"
        private const val WRAP_KEY_ALIAS = "vcfeditor_backup_seed_wrap"
        private const val SEED_BYTES = 32
        private const val GCM_TAG_BITS = 128

        // v1 legacy: single seed (kept for migration)
        private val KEY_ENCRYPTED_SEED = stringPreferencesKey("encrypted_backup_seed")

        // v2+: rotating seeds, keyed by seedId
        private val KEY_CURRENT_SEED_ID = stringPreferencesKey("backup_seed_current_id")
        private val KEY_SEEDS_JSON = stringPreferencesKey("backup_seeds_json")

        private val KEY_WATERMARK_GALLERY = longPreferencesKey("incremental_watermark_gallery")
        private val KEY_WATERMARK_FILES = longPreferencesKey("incremental_watermark_files")

        private val KEY_GALLERY_RESTORE_HASHES = stringPreferencesKey("gallery_restore_hashes")

        private const val MAX_GALLERY_HASHES = 2000
    }

    /** Returns the current seedId + seed bytes. Creates a new seed if needed. */
    suspend fun getOrCreateCurrentBackupSeed(): Pair<String, ByteArray> {
        val prefs = context.backupDataStore.data.first()

        // Migrate legacy single seed to the rotating store on first run.
        val legacyEnc = prefs[KEY_ENCRYPTED_SEED]
        if (!legacyEnc.isNullOrBlank() && prefs[KEY_SEEDS_JSON].isNullOrBlank()) {
            try {
                val legacySeed = decryptSeed(legacyEnc)
                val id = UUID.randomUUID().toString()
                val enc = encryptSeed(legacySeed)
                context.backupDataStore.edit {
                    it[KEY_CURRENT_SEED_ID] = id
                    it[KEY_SEEDS_JSON] = JSONObject(mapOf(id to enc)).toString()
                }
                return Pair(id, legacySeed)
            } catch (_: Exception) {
                // If keystore was wiped, legacy seed cannot be recovered.
                // Fall through to generate a new seed.
            }
        }

        val currentId = prefs[KEY_CURRENT_SEED_ID]
        val seedsJson = prefs[KEY_SEEDS_JSON]
        if (!currentId.isNullOrBlank() && !seedsJson.isNullOrBlank()) {
            val seed = runCatching { getBackupSeedById(currentId) }.getOrNull()
            if (seed != null) return Pair(currentId, seed)
        }

        // No current seed or cannot decrypt (e.g., Android Keystore was wiped).
        val newSeed = ByteArray(SEED_BYTES).also { SecureRandom().nextBytes(it) }
        val newEnc = encryptSeed(newSeed)
        val newId = UUID.randomUUID().toString()
        context.backupDataStore.edit {
            it[KEY_CURRENT_SEED_ID] = newId
            it[KEY_SEEDS_JSON] = JSONObject(mapOf(newId to newEnc)).toString()
            it.remove(KEY_ENCRYPTED_SEED)
        }
        return Pair(newId, newSeed)
    }

    /** Resolve a seed by seedId. Throws if not found or not decryptable. */
    suspend fun getBackupSeedById(seedId: String): ByteArray {
        val prefs = context.backupDataStore.data.first()
        val seedsJson = prefs[KEY_SEEDS_JSON]
        if (!seedsJson.isNullOrBlank()) {
            val obj = JSONObject(seedsJson)
            val enc = obj.optString(seedId, "")
            if (!enc.isNullOrBlank())
                return decryptSeed(enc)
        }

        // Fallback to legacy single seed if present.
        val legacy = prefs[KEY_ENCRYPTED_SEED]
        if (!legacy.isNullOrBlank())
            return decryptSeed(legacy)

        throw IllegalStateException("Backup seed not found (seedId=$seedId). The app may have been reset.")
    }

    /**
     * Imports a seed into the rotating store under [seedId]. This enables cross-device restore.
     * If a seed already exists for this id, this is a no-op.
     */
    suspend fun importBackupSeed(seedId: String, seed: ByteArray) {
        require(seed.size == SEED_BYTES) { "Invalid seed length: ${seed.size}" }

        val enc = encryptSeed(seed)

        context.backupDataStore.edit { prefs ->
            val currentJson = prefs[KEY_SEEDS_JSON]
            val obj = if (!currentJson.isNullOrBlank()) JSONObject(currentJson) else JSONObject()

            // Do not overwrite existing entries.
            val existing = obj.optString(seedId, "")
            if (existing.isNullOrBlank()) {
                obj.put(seedId, enc)
                prefs[KEY_SEEDS_JSON] = obj.toString()
            }

            // Ensure we have a current seed id so subsequent operations have a stable reference.
            if (prefs[KEY_CURRENT_SEED_ID].isNullOrBlank()) {
                prefs[KEY_CURRENT_SEED_ID] = seedId
            }

            // Remove legacy single-seed storage once we have the rotating store.
            prefs.remove(KEY_ENCRYPTED_SEED)
        }
    }

    suspend fun isGalleryHashRestored(sha256Hex: String): Boolean {
        val prefs = context.backupDataStore.data.first()
        val raw = prefs[KEY_GALLERY_RESTORE_HASHES] ?: return false
        return raw.lineSequence().any { it == sha256Hex }
    }

    suspend fun addGalleryRestoredHash(sha256Hex: String) {
        context.backupDataStore.edit { prefs ->
            val current = prefs[KEY_GALLERY_RESTORE_HASHES]
                ?.lineSequence()
                ?.filter { it.isNotBlank() }
                ?.toList()
                ?: emptyList()

            val next = sequenceOf(sha256Hex)
                .plus(current.asSequence().filter { it != sha256Hex })
                .take(MAX_GALLERY_HASHES)
                .toList()

            prefs[KEY_GALLERY_RESTORE_HASHES] = next.joinToString("\n")
        }
    }

    suspend fun getIncrementalWatermark(type: String): Long? {
        val prefs = context.backupDataStore.data.first()
        return when (type.lowercase()) {
            "gallery" -> prefs[KEY_WATERMARK_GALLERY]
            "files" -> prefs[KEY_WATERMARK_FILES]
            else -> null
        }
    }

    suspend fun setIncrementalWatermark(type: String, watermarkMs: Long) {
        context.backupDataStore.edit { prefs ->
            when (type.lowercase()) {
                "gallery" -> prefs[KEY_WATERMARK_GALLERY] = watermarkMs
                "files" -> prefs[KEY_WATERMARK_FILES] = watermarkMs
            }
        }
    }

    // Legacy API kept for call sites that haven't been upgraded.
    // Uses the current rotating seed.
    suspend fun getOrCreateBackupSeed(): ByteArray = getOrCreateCurrentBackupSeed().second

    private fun getOrCreateWrapKey(): SecretKey {
        val ks = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        val existing = ks.getKey(WRAP_KEY_ALIAS, null) as? SecretKey
        if (existing != null) return existing

        val kg = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        val spec = KeyGenParameterSpec.Builder(
            WRAP_KEY_ALIAS,
            KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
        )
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
            .build()
        kg.init(spec)
        return kg.generateKey()
    }

    private fun encryptSeed(seed: ByteArray): String {
        val key = getOrCreateWrapKey()
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key)
        val iv = cipher.iv
        val ct = cipher.doFinal(seed)
        val ivB64 = Base64.encodeToString(iv, Base64.NO_WRAP)
        val ctB64 = Base64.encodeToString(ct, Base64.NO_WRAP)
        return "$ivB64:$ctB64"
    }

    private fun decryptSeed(encoded: String): ByteArray {
        val parts = encoded.split(':')
        require(parts.size == 2) { "Invalid encrypted seed format" }

        val iv = Base64.decode(parts[0], Base64.NO_WRAP)
        val ct = Base64.decode(parts[1], Base64.NO_WRAP)

        val key = getOrCreateWrapKey()
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(GCM_TAG_BITS, iv))
        return cipher.doFinal(ct)
    }
}
