package com.aeliavision.androiddeck.feature.server.service.routes

import io.ktor.server.application.ApplicationCall

typealias WithAuth = suspend (call: ApplicationCall, block: suspend () -> Unit) -> Unit
