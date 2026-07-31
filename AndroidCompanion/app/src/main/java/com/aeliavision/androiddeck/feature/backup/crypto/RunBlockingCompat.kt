package com.aeliavision.androiddeck.feature.backup.crypto

import kotlinx.coroutines.runBlocking

// Small helper to avoid adding runBlocking imports in multiple files when bridging
// suspend key-derivation into existing non-suspend call sites.
internal fun <T> runBlockingCompat(block: suspend () -> T): T = runBlocking { block() }
