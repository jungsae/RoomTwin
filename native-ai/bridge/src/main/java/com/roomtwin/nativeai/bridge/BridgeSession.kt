package com.roomtwin.nativeai.bridge

import com.roomtwin.nativeai.GemmaRunner
import com.roomtwin.nativeai.InferenceBackend
import com.roomtwin.nativeai.RoomAnalysisParseResult
import java.util.concurrent.ExecutorService

internal data class BridgeConfig(val modelPath: String, val backend: InferenceBackend)

internal interface RoomEngine : AutoCloseable {
    fun initialize()
    fun analyze(request: ImageRequest): RoomAnalysisParseResult
}

internal class BridgeSession(
    private val worker: ExecutorService,
    private val open: (BridgeConfig) -> RoomEngine,
) {
    private enum class State { NEW, INITIALIZING, READY, ANALYZING, FAILED, RELEASING, RELEASED, RELEASE_FAILED }

    private class Work(val input: ImageRequest, var started: Boolean = false, var cancelled: Boolean = false)
    private data class Completion(val id: String, val json: String)
    private val lock = Any()
    private var state = State.NEW
    private var config: BridgeConfig? = null
    private var activeBackend: InferenceBackend? = null
    private var active: Work? = null
    private var completion: Completion? = null
    private var lastError: String? = null
    // Accessed only on the worker.
    private var engine: RoomEngine? = null

    fun isReleased(): Boolean = synchronized(lock) { state == State.RELEASED }

    fun getStatus(): String = synchronized(lock) {
        BridgeProtocol.reply("ok", fields = mapOf(
            "state" to state.name,
            "requestedBackend" to config?.backend?.name,
            "activeBackend" to activeBackend?.name,
            "activeRequestId" to active?.input?.id,
            "resultRequestId" to completion?.id,
            "cancelRequested" to (active?.cancelled == true),
            "nativeCancellation" to false,
            "lastError" to lastError,
        ))
    }

    fun initialize(requested: BridgeConfig): String = synchronized(lock) {
        when (state) {
            State.READY -> return if (requested == config) BridgeProtocol.reply("ok")
                else BridgeProtocol.error("ALREADY_INITIALIZED")
            State.INITIALIZING -> return if (requested == config) BridgeProtocol.reply("accepted")
                else BridgeProtocol.error("BUSY")
            State.ANALYZING -> return BridgeProtocol.error("BUSY")
            State.RELEASED, State.RELEASING, State.RELEASE_FAILED -> return BridgeProtocol.error("SESSION_CLOSED")
            State.NEW, State.FAILED -> Unit
        }
        if (completion != null) return BridgeProtocol.error("RESULT_PENDING")
        config = requested
        state = State.INITIALIZING
        lastError = null
        worker.execute { load(requested) }
        BridgeProtocol.reply("accepted")
    }

    fun analyze(request: ImageRequest): String = synchronized(lock) {
        when (state) {
            State.ANALYZING, State.INITIALIZING -> return BridgeProtocol.error("BUSY", request.id)
            State.RELEASED, State.RELEASING, State.RELEASE_FAILED -> return BridgeProtocol.error("SESSION_CLOSED", request.id)
            State.NEW, State.FAILED -> return BridgeProtocol.error("NOT_READY", request.id)
            State.READY -> Unit
        }
        if (completion != null) return BridgeProtocol.error("RESULT_PENDING", request.id)
        val accepted = Work(request)
        active = accepted
        state = State.ANALYZING
        worker.execute { run(accepted) }
        BridgeProtocol.reply("accepted", request.id)
    }

    fun takeResult(id: String): String = synchronized(lock) {
        val result = completion
        if (result?.id == id) {
            completion = null
            return result.json
        }
        if (state == State.ANALYZING && active?.input?.id == id) return BridgeProtocol.reply("pending", id)
        BridgeProtocol.error("RESULT_NOT_FOUND", id)
    }

    fun cancel(id: String): String = synchronized(lock) {
        val work = active
        if (state != State.ANALYZING || work?.input?.id != id) {
            return BridgeProtocol.error("REQUEST_NOT_ACTIVE", id)
        }
        work.cancelled = true
        BridgeProtocol.reply("accepted", id, mapOf("mode" to if (work.started) "DISCARD_ON_COMPLETION" else "SKIP_IF_QUEUED"))
    }

    fun release(): String = synchronized(lock) {
        if (state == State.RELEASED) return BridgeProtocol.reply("ok")
        if (state == State.RELEASING) return BridgeProtocol.reply("accepted")
        state = State.RELEASING
        completion = null
        worker.execute {
            val closed = closeEngine()
            synchronized(lock) {
                if (closed) {
                    activeBackend = null
                    active = null
                    lastError = null
                    state = State.RELEASED
                    worker.shutdown()
                } else {
                    lastError = "RESOURCE_RELEASE_FAILED"
                    state = State.RELEASE_FAILED
                }
            }
        }
        BridgeProtocol.reply("accepted")
    }

    private fun load(requested: BridgeConfig) {
        if (synchronized(lock) { state == State.RELEASING }) return
        val error = try {
            val opened = open(requested)
            engine = opened
            opened.initialize()
            null
        } catch (failure: Throwable) {
            errorCode(failure, "MODEL_INIT_FAILED")
        }
        val closed = error == null || closeEngine()
        synchronized(lock) {
            if (state == State.RELEASING) return
            activeBackend = if (error == null) requested.backend else null
            lastError = if (closed) error else "RESOURCE_RELEASE_FAILED"
            state = when {
                !closed -> State.RELEASE_FAILED
                error != null -> State.FAILED
                else -> State.READY
            }
        }
    }

    private fun run(work: Work) {
        val id = work.input.id
        synchronized(lock) {
            if (state == State.RELEASING) return
            if (work.cancelled) {
                finish(work, BridgeProtocol.error("CANCELLED", id), State.READY)
                return
            }
            work.started = true
        }
        var next = State.READY
        var error: String? = null
        val json = try {
            when (val parsed = checkNotNull(engine).analyze(work.input)) {
                is RoomAnalysisParseResult.Success -> BridgeProtocol.reply("ok", id, mapOf("result" to parsed.analysis))
                is RoomAnalysisParseResult.Failure -> {
                    error = "MODEL_OUTPUT_INVALID"
                    BridgeProtocol.error(error, id, parsed.path)
                }
            }
        } catch (failure: Throwable) {
            error = errorCode(failure, "INFERENCE_FAILED")
            if (failure is OutOfMemoryError || failure is LinkageError) {
                next = if (closeEngine()) State.FAILED else State.RELEASE_FAILED
            }
            BridgeProtocol.error(error, id)
        }
        synchronized(lock) {
            if (state == State.RELEASING) return
            lastError = if (next == State.RELEASE_FAILED) "RESOURCE_RELEASE_FAILED" else error
            if (next == State.FAILED) activeBackend = null
            finish(work, json, next)
        }
    }

    private fun finish(work: Work, json: String, next: State) {
        val id = work.input.id
        completion = Completion(id, if (work.cancelled) BridgeProtocol.error("CANCELLED", id) else json)
        active = null
        state = next
    }

    private fun closeEngine(): Boolean = try {
        engine?.close()
        engine = null
        true
    } catch (failure: Throwable) {
        errorCode(failure, "RESOURCE_RELEASE_FAILED")
        false
    }

    private fun errorCode(failure: Throwable, fallback: String): String = when (failure) {
        is GemmaRunner.Failure -> failure.code.name
        is OutOfMemoryError -> "RESOURCE_EXHAUSTED"
        is Exception, is LinkageError -> fallback
        else -> throw failure
    }
}
