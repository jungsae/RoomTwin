package com.roomtwin.nativeai

import android.app.Activity
import android.app.Instrumentation
import android.net.Uri
import android.os.Bundle
import android.os.PowerManager
import com.google.ai.edge.litertlm.ExperimentalApi
import com.google.ai.edge.litertlm.ExperimentalFlags
import com.google.gson.GsonBuilder
import java.io.File
import kotlin.time.measureTimedValue

/** A paired prompt evaluation on the fixed public images, in the field APK only. */
class RoomQualityInstrumentation : Instrumentation() {
    private lateinit var arguments: Bundle

    override fun onCreate(arguments: Bundle) {
        super.onCreate(arguments)
        this.arguments = arguments
        start()
    }

    @OptIn(ExperimentalApi::class)
    override fun onStart() {
        val backend = InferenceBackend.valueOf(checkNotNull(arguments.getString("backend")))
        val fixtures = File(checkNotNull(targetContext.getExternalFilesDir(null)), "quality")
        val report = File(targetContext.filesDir, "quality-$backend.jsonl")
        val gson = GsonBuilder().serializeNulls().create()
        val power = targetContext.getSystemService(PowerManager::class.java)
        report.writeText("")
        fun record(fields: Map<String, Any?>) = report.appendText(gson.toJson(fields) + "\n")

        var completed = false
        try {
            ExperimentalFlags.enableBenchmark = true
            GemmaRunner(targetContext).use { runner ->
                runner.selectBackend(backend)
                // Discard one initialization request before measuring warm latency.
                runner.analyzeRoom(Uri.fromFile(File(fixtures, "empty.jpg")))
                val cases = listOf("empty", "bedroom", "living", "doors", "occluded", "cabinet", "kitchen")
                for ((index, id) in cases.withIndex()) {
                    val uri = Uri.fromFile(File(fixtures, "$id.jpg"))
                    val prompts = Prompt.entries
                    val order = if ((index + backend.ordinal) % 2 == 0) prompts else prompts.reversed()
                    for (variant in order) {
                        val key = mapOf("case" to id, "backend" to backend.name, "prompt" to variant.name)
                        val timed = measureTimedValue {
                            val result = runner.analyzeRoom(uri, variant.text)
                            val parsed = RoomAnalysisJson.parse(result.rawResponse)
                            if (parsed is RoomAnalysisParseResult.Success) RoomAnalysisJson.encode(parsed.analysis)
                            result to parsed
                        }
                        val (result, parsed) = timed.value
                        check(result.backend == backend && result.initializationMillis == null)
                        record(key + mapOf(
                            "inferMs" to result.inferenceMillis,
                            "totalMs" to timed.duration.inWholeMilliseconds,
                            "decodeTokens" to result.decodeTokens,
                            "charsUtf16" to result.rawResponse.length,
                            "schemaValid" to (parsed is RoomAnalysisParseResult.Success),
                            "parseFailure" to (parsed as? RoomAnalysisParseResult.Failure),
                            "thermal" to if (android.os.Build.VERSION.SDK_INT >= 29) power.currentThermalStatus else null,
                            "raw" to result.rawResponse,
                        ))
                    }
                }
            }
            completed = true
        } catch (error: Exception) {
            record(mapOf("error" to error.javaClass.simpleName, "cause" to error.cause?.javaClass?.simpleName))
        } finally {
            finish(if (completed) Activity.RESULT_OK else Activity.RESULT_CANCELED, Bundle().apply {
                putString("status", if (completed) "completed" else "failed")
            })
        }
    }

    private enum class Prompt(val text: String) {
        BASELINE(RoomAnalysisJson.prompt),
        CANDIDATE(RoomAnalysisJson.prompt + """

            Require visible evidence before adding an item to objects or openings:
            - A wall requires identifiable architectural surface in the frame. Keep a visible part of a wall
              if furniture hides the rest, but do not state what the hidden part or its outline looks like.
            - Cabinet doors, flat furniture panels and their seams are furniture, even when floor-to-ceiling.
              A room door requires doorway evidence, such as a frame and a door leaf or a passage into a room.
              A window requires visible glazing or a recognizable window frame. Omit unobserved openings.
            - Mention a wall corner only when the junction of two visible wall faces is identifiable.
              Do not treat cabinet edges, floor trim, window frames or shadows as wall corners.
            - List each distinct visible item once. If its type or boundary is ambiguous, use
              uncertainObservations instead of asserting an object, opening or hidden boundary.
        """.trimIndent().let { "\n\n$it" }),
    }
}
