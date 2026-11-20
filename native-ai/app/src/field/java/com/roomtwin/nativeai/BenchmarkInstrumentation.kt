package com.roomtwin.nativeai

import android.app.Activity
import android.app.Instrumentation
import android.content.Intent
import android.content.IntentFilter
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.BatteryManager
import android.os.Build
import android.os.Bundle
import android.os.Debug
import android.os.PowerManager
import android.os.SystemClock
import com.google.ai.edge.litertlm.Engine
import com.google.ai.edge.litertlm.ExperimentalApi
import com.google.ai.edge.litertlm.ExperimentalFlags
import com.google.ai.edge.litertlm.LogSeverity
import com.google.gson.GsonBuilder
import java.io.File
import java.util.concurrent.CancellationException
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicLong
import kotlin.time.measureTimedValue

/** Installed only in the side-by-side field variant. All inputs are fixed public fixtures. */
class BenchmarkInstrumentation : Instrumentation() {
    private lateinit var args: Bundle
    private lateinit var output: File
    private val gson = GsonBuilder().serializeNulls().create()

    override fun onCreate(arguments: Bundle) {
        super.onCreate(arguments)
        args = arguments
        start()
    }

    @OptIn(ExperimentalApi::class)
    override fun onStart() {
        val backend = InferenceBackend.valueOf(checkNotNull(args.getString("backend")))
        output = File(targetContext.filesDir, "bench-$backend.jsonl")
        output.writeText("")
        val power = targetContext.getSystemService(PowerManager::class.java)
        var success = false
        try {
            ExperimentalFlags.enableBenchmark = true
            Engine.setNativeMinLogSeverity(LogSeverity.INFO)
            GemmaRunner(targetContext).use { runner ->
                runner.selectBackend(backend)
                val directory = checkNotNull(targetContext.getExternalFilesDir(null))
                val schedule = listOf("bedroom", "bedroom", "living", "bedroom", "living", "bedroom")
                for ((index, photo) in schedule.withIndex()) {
                    val file = File(directory, "$photo.jpg")
                    val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                    BitmapFactory.decodeFile(file.path, bounds)
                    val request = mapOf("index" to index + 1, "requested" to backend.name, "image" to photo,
                        "width" to bounds.outWidth, "height" to bounds.outHeight, "modelRevision" to "6e5c4f1")
                    record(request + mapOf("state" to "started"))
                    val peak = AtomicLong(0)
                    val sampler = Executors.newSingleThreadScheduledExecutor()
                    val start = SystemClock.elapsedRealtime()
                    try {
                        sampler.scheduleWithFixedDelay({
                            peak.accumulateAndGet(Debug.getPss(), ::maxOf)
                        }, 0, 1, TimeUnit.SECONDS)
                        val timed = measureTimedValue {
                            val inference = runner.analyzeRoom(Uri.fromFile(file))
                            val parsed = RoomAnalysisJson.parse(inference.rawResponse)
                            if (parsed is RoomAnalysisParseResult.Success) RoomAnalysisJson.encode(parsed.analysis)
                            inference to parsed
                        }
                        val (run, parsed) = timed.value
                        val valid = parsed is RoomAnalysisParseResult.Success
                        val battery = targetContext.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
                        record(request + mapOf(
                            "state" to if (valid) "ok" else "invalid", "backend" to run.backend.name,
                            "initMs" to run.initializationMillis, "imageMs" to run.imageMillis,
                            "inferMs" to run.inferenceMillis, "totalMs" to timed.duration.inWholeMilliseconds,
                            "charsUtf16" to run.rawResponse.length, "decodeTokens" to run.decodeTokens,
                            "schemaValid" to valid, "raw" to run.rawResponse, "pssKiB" to Debug.getPss(),
                            "sampleMaxPssKiB" to peak.get(), "batteryDeciC" to battery?.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, -1),
                            "thermal" to if (Build.VERSION.SDK_INT >= 29) power.currentThermalStatus else null,
                        ))
                        check(valid) { "schema" }
                    } catch (error: Throwable) {
                        record(request + mapOf("state" to if (error is CancellationException) "cancelled" else "failed",
                            "elapsedMs" to SystemClock.elapsedRealtime() - start, "error" to error.javaClass.simpleName,
                            "cause" to error.cause?.javaClass?.simpleName))
                        throw error
                    } finally {
                        sampler.shutdownNow()
                        sampler.awaitTermination(5, TimeUnit.SECONDS)
                    }
                }
                if (args.getString("switch") == "true") {
                    // Run outside measured samples. Use the same engine owner as the selector.
                    val other = if (backend == InferenceBackend.CPU) InferenceBackend.GPU else InferenceBackend.CPU
                    for (selected in listOf(other, backend)) {
                        runner.selectBackend(selected)
                        val text = runner.infer("Reply with the word apple.", null)
                        val image = runner.infer("What color is the sofa? Answer with one word.",
                            Uri.fromFile(File(directory, "living.jpg")))
                        check(text.backend == selected && text.initializationMillis != null && image.initializationMillis == null)
                        record(mapOf("state" to "switch", "backend" to selected, "text" to text.rawResponse, "image" to image.rawResponse))
                    }
                }
            }
            success = true
        } catch (error: Exception) {
            record(mapOf("state" to "runFailed", "error" to error.javaClass.simpleName))
        } finally {
            finish(if (success) Activity.RESULT_OK else Activity.RESULT_CANCELED,
                Bundle().apply { putString("status", if (success) "completed" else "failed") })
        }
    }

    private fun record(fields: Map<String, Any?>) {
        output.appendText(gson.toJson(fields) + "\n")
    }
}
