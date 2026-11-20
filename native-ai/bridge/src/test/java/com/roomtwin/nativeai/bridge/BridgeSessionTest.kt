package com.roomtwin.nativeai.bridge

import com.google.gson.JsonObject
import com.google.gson.JsonParser
import com.roomtwin.nativeai.GemmaRunner
import com.roomtwin.nativeai.InferenceBackend
import com.roomtwin.nativeai.RoomAnalysis
import com.roomtwin.nativeai.RoomAnalysisParseResult
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.ArrayDeque
import java.util.concurrent.AbstractExecutorService
import java.util.concurrent.TimeUnit

class BridgeSessionTest {
    private val worker = ManualExecutor()
    private val events = mutableListOf<String>()
    private val gpu = BridgeConfig("/files/gemma.litertlm", InferenceBackend.GPU)
    private val cpu = gpu.copy(backend = InferenceBackend.CPU)
    private val image = ImageRequest("photo-1", "content://capture/test.jpg", "image/jpeg")
    private var start: () -> Unit = {}
    private var analyze: () -> RoomAnalysisParseResult = {
        RoomAnalysisParseResult.Success(RoomAnalysis("1.0", "bedroom", emptyList(), emptyList(), emptyList()))
    }
    private var close: () -> Unit = {}

    private val bridge = BridgeSession(worker) { config ->
        object : RoomEngine {
            override fun initialize() {
                events.add("init-${config.backend}")
                start()
            }

            override fun analyze(request: ImageRequest): RoomAnalysisParseResult {
                events.add("analyze")
                return analyze()
            }

            override fun close() {
                close.invoke()
                events.add("close")
            }
        }
    }

    @Test fun initializesOnceAndRequiresReleaseForDifferentConfig() {
        assertEquals("NEW", bridge.state())
        assertEquals("accepted", bridge.initialize(cpu).status())
        assertEquals("accepted", bridge.initialize(cpu).status())
        assertEquals("BUSY", bridge.initialize(gpu).code())
        assertEquals("BUSY", bridge.analyze(image).code())
        assertTrue(events.isEmpty())
        worker.runNext()
        assertEquals("READY", bridge.state())
        assertEquals("CPU", bridge.getStatus().field("activeBackend"))
        assertEquals("ok", bridge.initialize(cpu).status())
        assertEquals("ALREADY_INITIALIZED", bridge.initialize(gpu).code())
        assertEquals(listOf("init-CPU"), events)
    }

    @Test fun gpuFailureReportsRequestedBackendAndExplicitRetry() {
        start = { throw GemmaRunner.Failure(GemmaRunner.Error.MODEL_INIT_FAILED) }
        bridge.initialize(gpu)
        worker.runNext()
        val status = bridge.getStatus().json()
        assertEquals("FAILED", bridge.state())
        assertEquals("GPU", status["requestedBackend"].asString)
        assertTrue(status["activeBackend"].isJsonNull)
        assertEquals("MODEL_INIT_FAILED", status["lastError"].asString)
        assertEquals("NOT_READY", bridge.analyze(image).code())
        assertEquals(listOf("init-GPU", "close"), events)
        start = {}
        ready(cpu)
        assertEquals("CPU", bridge.getStatus().field("activeBackend"))
    }

    @Test fun rejectsOverlapAndRetainsOneResultUntilConsumed() {
        ready(cpu)
        assertEquals("accepted", bridge.analyze(image).status())
        assertEquals("pending", bridge.takeResult(image.id).status())
        val next = image.copy(id = "photo-2")
        assertEquals("BUSY", bridge.analyze(next).code())
        worker.runNext()
        assertEquals("RESULT_PENDING", bridge.analyze(next).code())
        assertEquals("RESULT_NOT_FOUND", bridge.takeResult(next.id).code())
        assertEquals("bedroom", bridge.takeResult(image.id).json().getAsJsonObject("result")["sceneType"].asString)
        assertEquals("RESULT_NOT_FOUND", bridge.takeResult(image.id).code())
        assertEquals("accepted", bridge.analyze(next).status())
        worker.runNext()
        assertEquals(listOf("init-CPU", "analyze", "analyze"), events)
    }

    @Test fun queuedCancellationSkipsInference() {
        ready(cpu)
        bridge.analyze(image)
        assertEquals("REQUEST_NOT_ACTIVE", bridge.cancel("foreign").code())
        assertEquals("SKIP_IF_QUEUED", bridge.cancel(image.id).field("mode"))
        worker.runNext()
        assertEquals("CANCELLED", bridge.takeResult(image.id).code())
        assertEquals(listOf("init-CPU"), events)
        assertEquals("READY", bridge.state())
    }

    @Test fun runningCancellationHoldsSlotUntilInferenceReturns() {
        ready(cpu)
        analyze = {
            assertEquals("DISCARD_ON_COMPLETION", bridge.cancel(image.id).field("mode"))
            assertEquals("BUSY", bridge.analyze(image.copy(id = "photo-2")).code())
            assertEquals("pending", bridge.takeResult(image.id).status())
            RoomAnalysisParseResult.Failure(RoomAnalysisParseResult.Failure.Kind.EMPTY)
        }
        bridge.analyze(image)
        worker.runNext()
        assertEquals("CANCELLED", bridge.takeResult(image.id).code())
        assertEquals("READY", bridge.state())
        assertFalse(bridge.getStatus().json()["nativeCancellation"].asBoolean)
    }

    @Test fun outputAndImageErrorsExposeStableCodes() {
        ready(cpu)
        analyze = { RoomAnalysisParseResult.Failure(RoomAnalysisParseResult.Failure.Kind.WRONG_TYPE, "$.objects") }
        bridge.analyze(image)
        worker.runNext()
        val error = bridge.takeResult(image.id).json().getAsJsonObject("error")
        assertEquals("MODEL_OUTPUT_INVALID", error["code"].asString)
        assertEquals("$.objects", error["fieldPath"].asString)
        analyze = { throw GemmaRunner.Failure(GemmaRunner.Error.IMAGE_UNREADABLE) }
        bridge.analyze(image.copy(id = "photo-2"))
        worker.runNext()
        assertEquals("IMAGE_UNREADABLE", bridge.takeResult("photo-2").code())
        assertEquals("READY", bridge.state())
    }

    @Test fun memoryErrorClosesEngineAndRequiresInitialization() {
        ready(cpu)
        analyze = { throw OutOfMemoryError() }
        bridge.analyze(image)
        worker.runNext()
        assertEquals("FAILED", bridge.state())
        assertEquals("RESOURCE_EXHAUSTED", bridge.takeResult(image.id).code())
        assertEquals("NOT_READY", bridge.analyze(image).code())
        assertEquals(listOf("init-CPU", "analyze", "close"), events)
    }

    @Test fun releaseDrainsNativeCallAndDropsItsResult() {
        ready(cpu)
        analyze = {
            assertEquals("accepted", bridge.release().status())
            assertEquals("RELEASING", bridge.state())
            assertEquals("SESSION_CLOSED", bridge.analyze(image).code())
            assertEquals(listOf("init-CPU", "analyze"), events)
            RoomAnalysisParseResult.Failure(RoomAnalysisParseResult.Failure.Kind.EMPTY)
        }
        bridge.analyze(image)
        worker.runNext()
        assertEquals("RELEASING", bridge.state())
        worker.runNext()
        assertEquals("RELEASED", bridge.state())
        assertEquals("RESULT_NOT_FOUND", bridge.takeResult(image.id).code())
        assertEquals("ok", bridge.release().status())
        assertTrue(worker.isShutdown)
        assertEquals(listOf("init-CPU", "analyze", "close"), events)
    }

    @Test fun releaseFailureCanRetryWithoutCreatingAnotherEngine() {
        ready(cpu)
        close = { throw IllegalStateException() }
        bridge.release()
        worker.runNext()
        assertEquals("RELEASE_FAILED", bridge.state())
        assertEquals("SESSION_CLOSED", bridge.initialize(gpu).code())
        close = {}
        bridge.release()
        worker.runNext()
        assertEquals("RELEASED", bridge.state())
        assertEquals(listOf("init-CPU", "close"), events)
    }

    private fun ready(config: BridgeConfig) {
        assertEquals("accepted", bridge.initialize(config).status())
        worker.runNext()
        assertEquals("READY", bridge.state())
    }

    private fun BridgeSession.state() = getStatus().field("state")
    private fun String.json(): JsonObject = JsonParser.parseString(this).asJsonObject
    private fun String.field(key: String) = json()[key].asString
    private fun String.status() = field("status")
    private fun String.code() = json().getAsJsonObject("error")["code"].asString

    private class ManualExecutor : AbstractExecutorService() {
        private val tasks = ArrayDeque<Runnable>()
        private var closed = false
        override fun execute(task: Runnable) {
            check(!closed)
            tasks.add(task)
        }

        fun runNext() = tasks.remove().run()

        override fun shutdown() { closed = true }
        override fun shutdownNow(): MutableList<Runnable> {
            shutdown()
            return tasks.toMutableList().also { tasks.clear() }
        }

        override fun isShutdown() = closed
        override fun isTerminated() = closed && tasks.isEmpty()
        override fun awaitTermination(timeout: Long, unit: TimeUnit) = isTerminated
    }
}
