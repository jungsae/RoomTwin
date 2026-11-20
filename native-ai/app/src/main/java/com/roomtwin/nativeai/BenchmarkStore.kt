package com.roomtwin.nativeai

import com.google.gson.GsonBuilder
import java.io.BufferedWriter
import java.io.File
import java.io.OutputStream
import java.util.UUID

internal data class BenchModelIdentity(val name: String, val bytes: Long?, val lastModifiedMillis: Long?)

internal data class BenchRequestRecord(
    val workload: BenchWorkload,
    val order: BenchOrder,
    val contextLength: Int,
    val maxOutputTokens: Int,
    val temperature: Double,
    val topK: Int,
    val topP: Double,
    val thinking: Boolean,
    val systemPromptPresent: Boolean,
    val warmUps: Int,
    val repeats: Int,
    val contextSweep: Boolean,
    val thinkingPair: Boolean,
    val needlePosition: NeedlePosition,
    val visionCase: VisionCase?,
    val plannedTrials: Int,
    val imageCount: Int,
    val inputType: String,
)

internal data class BenchSessionHeader(
    val id: String,
    val createdUtc: String,
    val gitSha: String,
    val appVersion: String,
    val model: BenchModelIdentity,
    val device: String,
    val os: String,
    val liteRt: String,
    val request: BenchRequestRecord,
    val schema: Int = 1,
)

internal data class BenchSession(val header: BenchSessionHeader, val trials: List<BenchTrial>, val end: BenchEnd)
private data class BenchRecord(
    val type: String,
    val header: BenchSessionHeader? = null,
    val trial: BenchTrial? = null,
    val end: BenchEnd? = null,
)

internal class BenchStore(private val directory: File) {
    private val gson = GsonBuilder().serializeNulls().create()

    fun begin(header: BenchSessionHeader): Journal {
        check(directory.isDirectory || directory.mkdirs())
        val destination = file(header.id)
        check(destination.createNewFile())
        val journal = Journal(destination.bufferedWriter())
        try {
            journal.header(header)
        } catch (error: Throwable) {
            journal.close()
            throw error
        }
        return journal
    }

    fun headers(): List<BenchSessionHeader> = sessionFiles().mapNotNull { path ->
        runCatching { path.bufferedReader().use { read(it.readLine())?.header } }.getOrNull()
    }.sortedByDescending(BenchSessionHeader::createdUtc)

    fun session(id: String): BenchSession {
        val path = file(id)
        return path.useLines { lines ->
            val iterator = lines.iterator()
            val header = requireNotNull(if (iterator.hasNext()) read(iterator.next())?.header else null)
            require(header.id == id)
            val trials = mutableListOf<BenchTrial>()
            var end = BenchEnd.INCOMPLETE
            while (iterator.hasNext()) {
                val record = read(iterator.next()) ?: continue
                record.trial?.let(trials::add)
                record.end?.let { end = it }
            }
            BenchSession(header, trials, end)
        }
    }

    fun export(id: String, csv: Boolean, destination: OutputStream) {
        if (!csv) {
            file(id).inputStream().use { it.copyTo(destination) }
            return
        }
        val session = session(id)
        val header = session.header
        destination.bufferedWriter(Charsets.UTF_8).use { writer ->
            writer.appendLine(CSV_COLUMNS.joinToString(","))
            val metadata = listOf(
                header.id, header.createdUtc, header.gitSha, header.appVersion, gson.toJson(header.model),
                header.device, header.os, header.liteRt, gson.toJson(header.request), session.end,
            )
            fun write(trial: BenchTrial?, turn: BenchTurn?) {
                val native = turn?.native
                writer.appendLine(csvRow(metadata + listOf(
                    trial?.index, trial?.condition?.backend, trial?.condition?.contextLength, trial?.condition?.thinking,
                    trial?.condition?.loadPercent, trial?.condition?.method, trial?.phase, trial?.outcome,
                    trial?.errorCode, trial?.latencyMillis, trial?.evaluationCorrect, trial?.outputTokens,
                    trial?.before?.let(gson::toJson), trial?.after?.let(gson::toJson), trial?.peakSampledPssKiB,
                    trial?.peakSampledRssKiB, trial?.peakThermalStatus,
                    turn?.turn, turn?.inputCharacters, turn?.imageCount, turn?.latencyMillis,
                    native?.initializationMillis, turn?.actualTtftMillis, native?.runtimeTtftEstimateMillis, turn?.firstChunkMillis,
                    native?.prefillMillis, native?.decodeMillis, native?.inputTokens, native?.outputTokens,
                    native?.prefillTokensPerSecond, native?.decodeTokensPerSecond, native?.contextTokens,
                    native?.contextCapacity, trial?.after?.gpuDedicatedMemoryBytes,
                )))
            }
            if (session.trials.isEmpty()) write(null, null)
            for (trial in session.trials) {
                if (trial.turns.isEmpty()) write(trial, null) else trial.turns.forEach { write(trial, it) }
            }
        }
    }

    fun delete(id: String): Boolean = file(id).delete()

    fun deleteAll(): Int = sessionFiles().count { it.delete() }

    inner class Journal(private val writer: BufferedWriter) : AutoCloseable {
        fun header(header: BenchSessionHeader) = append(BenchRecord("session", header = header))
        fun trial(trial: BenchTrial) = append(BenchRecord("trial", trial = trial))
        fun end(end: BenchEnd) = append(BenchRecord("end", end = end))
        private fun append(record: BenchRecord) {
            writer.appendLine(gson.toJson(record))
            writer.flush()
        }
        override fun close() = writer.close()
    }

    private fun file(id: String): File {
        require(runCatching { UUID.fromString(id).toString() == id }.getOrDefault(false))
        return File(directory, "$id.jsonl")
    }

    private fun sessionFiles(): List<File> = directory.listFiles { path ->
        path.isFile && path.extension == "jsonl" &&
            runCatching { UUID.fromString(path.nameWithoutExtension).toString() == path.nameWithoutExtension }.getOrDefault(false)
    }?.toList().orEmpty()

    private fun read(line: String?): BenchRecord? = try {
        gson.fromJson(line, BenchRecord::class.java)
    } catch (_: RuntimeException) {
        // A process death can leave a partial final line. Earlier measurements remain usable.
        null
    }

    companion object {
        internal fun csvRow(values: List<Any?>) = values.joinToString(",") { value ->
            if (value == null) "N/A" else "\"" + value.toString().replace("\"", "\"\"") + "\""
        }

        private val CSV_COLUMNS = listOf(
            "session_id", "created_utc", "git_sha", "app_version", "model", "device", "os", "litert_lm",
            "request_settings", "session_end", "trial", "backend", "requested_context", "thinking", "estimated_load_percent",
            "comparison_method", "phase", "outcome", "error_code", "trial_ms", "evaluation_correct", "trial_output_tokens",
            "before_resources", "after_resources", "sampled_peak_pss_kib", "sampled_peak_rss_kib", "peak_thermal_status",
            "turn", "input_characters", "image_count", "request_ms", "model_initialization_ms",
            "actual_ttft_ms", "litert_ttft_estimate_ms", "first_visible_chunk_ms", "prefill_ms", "decode_ms",
            "input_tokens", "output_tokens", "prefill_tokens_per_second", "decode_tokens_per_second",
            "context_used_tokens", "context_capacity_tokens", "gpu_dedicated_memory",
        )
    }
}
