package com.aeliavision.androiddeck.feature.server.service

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.math.BigInteger
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.X509Certificate
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLParameters
import javax.net.ssl.SSLServerSocketFactory
import javax.security.auth.x500.X500Principal

object TlsHelper {
    internal const val KEY_ALIAS = "vcfeditor_tls"

    @Deprecated("Use getKeystorePassword(context) — kept only for legacy migration.")
    internal val LEGACY_KEYSTORE_PASSWORD = charArrayOf('v', 'c', 'f', 'e', 'd', 'i', 't', 'o', 'r')

    fun getKeystorePassword(context: Context): CharArray =
        KeystorePasswordProvider.getOrCreatePassword(context)

    fun createServerSocketFactory(context: Context): SSLServerSocketFactory {
        val keyStore = loadOrCreateKeyStore(context)
        val password = getKeystorePassword(context)

        val kmf = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm())
        kmf.init(keyStore, password)

        val sslContext = SSLContext.getInstance("TLS")
        sslContext.init(kmf.keyManagers, null, SecureRandom())

        return sslContext.serverSocketFactory
    }

    suspend fun createSslContext(context: Context): SSLContext = withContext(Dispatchers.IO) {
        val keyStore = loadOrCreateKeyStore(context)
        val password = getKeystorePassword(context)
        val kmf = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm())
        kmf.init(keyStore, password)
        val sslContext = SSLContext.getInstance("TLS")
        sslContext.init(kmf.keyManagers, null, SecureRandom())
        sslContext
    }

    suspend fun createHttp11OnlySslServerSocketFactory(context: Context): SSLServerSocketFactory =
        withContext(Dispatchers.IO) {
            val keyStore = loadOrCreateKeyStore(context)
            val password = getKeystorePassword(context)
            val kmf = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm())
            kmf.init(keyStore, password)
            val sslContext = SSLContext.getInstance("TLS")
            sslContext.init(kmf.keyManagers, null, SecureRandom())
            object : SSLServerSocketFactory() {
                private val delegate = sslContext.serverSocketFactory
                private fun applyHttp11(s: javax.net.ssl.SSLServerSocket) = s.also {
                    val params = SSLParameters()
                    params.applicationProtocols = arrayOf("http/1.1")
                    it.sslParameters = params
                }
                override fun getDefaultCipherSuites() = delegate.defaultCipherSuites
                override fun getSupportedCipherSuites() = delegate.supportedCipherSuites
                override fun createServerSocket() =
                    applyHttp11(delegate.createServerSocket() as javax.net.ssl.SSLServerSocket)
                override fun createServerSocket(port: Int) =
                    applyHttp11(delegate.createServerSocket(port) as javax.net.ssl.SSLServerSocket)
                override fun createServerSocket(port: Int, backlog: Int) =
                    applyHttp11(delegate.createServerSocket(port, backlog) as javax.net.ssl.SSLServerSocket)
                override fun createServerSocket(port: Int, backlog: Int, ifAddress: java.net.InetAddress) =
                    applyHttp11(delegate.createServerSocket(port, backlog, ifAddress) as javax.net.ssl.SSLServerSocket)
            }
        }

    suspend fun loadOrCreateServerKeyStore(context: Context): KeyStore = withContext(Dispatchers.IO) {
        loadOrCreateKeyStore(context)
    }

    suspend fun getKeystorePasswordAsync(context: Context): CharArray = withContext(Dispatchers.IO) {
        getKeystorePassword(context)
    }

    suspend fun getCertificateSha256Fingerprint(context: Context): String = withContext(Dispatchers.IO) {
        val keyStore = loadOrCreateKeyStore(context)
        val cert = keyStore.getCertificate(KEY_ALIAS) as? X509Certificate ?: return@withContext ""
        val digest = MessageDigest.getInstance("SHA-256").digest(cert.encoded)
        digest.joinToString(separator = "") { b -> "%02X".format(b) }
    }

    private fun loadOrCreateKeyStore(context: Context): KeyStore {
        val keyStore = KeyStore.getInstance("AndroidKeyStore")
        keyStore.load(null)

        if (!keyStore.containsAlias(KEY_ALIAS)) {
            val kpg = KeyPairGenerator.getInstance(
                KeyProperties.KEY_ALGORITHM_RSA,
                "AndroidKeyStore"
            )
            val spec = KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY or KeyProperties.PURPOSE_DECRYPT
            )
                .setDigests(KeyProperties.DIGEST_SHA256)
                .setSignaturePaddings(KeyProperties.SIGNATURE_PADDING_RSA_PKCS1)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_RSA_PKCS1)
                .setKeySize(2048)
                .setCertificateSubject(X500Principal("CN=AndroidDeck Companion"))
                .setCertificateSerialNumber(BigInteger.valueOf(System.currentTimeMillis()))
                .build()
            kpg.initialize(spec)
            kpg.generateKeyPair()
        }

        return keyStore
    }
}
