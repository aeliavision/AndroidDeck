package com.aeliavision.androiddeck.feature.server.service

import java.io.File

/**
 */
object PathSandbox {

    private val ALLOWED_ROOTS: List<String> by lazy {
        val roots = mutableListOf<String>()
        

        roots.add(android.os.Environment.getExternalStorageDirectory().canonicalPath)
        

        val storageDir = File("/storage")
        if (storageDir.exists() && storageDir.isDirectory) {
            storageDir.listFiles()?.forEach { file ->
                try {
                    val canonical = file.canonicalPath
                    if (canonical != "/storage/self" && canonical != "/storage/emulated") {
                        roots.add(canonical)
                    }
                } catch (_: Exception) {}
            }
        }
        
        roots.distinct()
    }

    fun validate(path: String): String {
        require(path.isNotBlank()) { "Path must not be blank." }

        val file = File(path)
        val canonical = try {
            file.canonicalPath
        } catch (_: Exception) {
            file.absolutePath
        }


        val allowed = ALLOWED_ROOTS.any { root ->
            canonical == root || canonical.startsWith("$root/")
        }

        if (!allowed) {
            throw SecurityException(
                "Access denied: '$canonical' is outside the allowed file-system roots. " +
                "Allowed roots: ${ALLOWED_ROOTS.joinToString()}"
            )
        }

        return canonical
    }

    fun validateDirectory(path: String): String {
        val canonical = validate(path)
        val file = File(canonical)
        require(!file.exists() || file.isDirectory) { "Not a directory: $canonical" }
        return canonical
    }

    fun validateFile(path: String): String {
        val canonical = validate(path)
        require(File(canonical).isFile) { "Not a regular file: $canonical" }
        return canonical
    }


}
