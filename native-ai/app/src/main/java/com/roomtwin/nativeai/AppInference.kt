package com.roomtwin.nativeai

import android.app.Application
import java.util.concurrent.Executors

// The same worker owns engine creation, conversation use, and release across Activity recreation.
internal class AppInference(private val application: Application) {
    private val worker = Executors.newSingleThreadExecutor()
    private var owner: Any? = null
    private var runner: GemmaRunner? = null

    fun <T> submit(
        owner: Any,
        action: (GemmaRunner) -> T,
        onResult: (Result<T>) -> Unit,
    ) {
        worker.execute {
            val result = runCatching {
                if (this.owner !== owner) {
                    close()
                    runner = GemmaRunner(application)
                    this.owner = owner
                }
                action(checkNotNull(runner))
            }
            if (result.exceptionOrNull() is OutOfMemoryError) runCatching { close() }
            onResult(result)
        }
    }

    fun execute(action: () -> Unit) {
        worker.execute(action)
    }

    fun reset(owner: Any) {
        worker.execute { if (this.owner === owner) runCatching { runner?.resetPlayground() } }
    }

    fun release(owner: Any) {
        worker.execute { if (this.owner === owner) runCatching { close() } }
    }

    private fun close() {
        runner?.close()
        runner = null
        owner = null
    }
}
