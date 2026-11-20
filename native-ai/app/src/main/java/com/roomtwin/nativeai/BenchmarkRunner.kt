package com.roomtwin.nativeai

import android.content.Context
import android.net.Uri
import android.os.Build
import java.io.File
import java.time.Instant
import java.util.Locale
import java.util.UUID
import java.util.concurrent.CancellationException
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference
import kotlin.time.TimeSource

internal class BenchControl {
    private val stopped = AtomicReference<BenchEnd?>(null)
    val reason: BenchEnd? get() = stopped.get()

    fun stop(reason: BenchEnd = BenchEnd.STOP_REQUESTED) {
        stopped.compareAndSet(null, reason)
    }

    fun check() {
        if (reason != null) throw CancellationException()
    }
}

internal data class BenchProgress(
    val index: Int,
    val planned: Int,
    val condition: BenchCondition,
    val phase: BenchPhase,
    val before: BenchResources,
    val completed: BenchTrial? = null,
    val sample: BenchResources? = null,
)

internal class BenchmarkRunner(private val app: Context, private val store: BenchStore) {
    private val sensors = BenchSensors(app)

    fun run(
        engine: GemmaRunner,
        config: BenchConfig,
        incoming: List<Uri>,
        truth: VisionTruth?,
        control: BenchControl,
        onProgress: (BenchProgress) -> Unit,
    ): BenchSession {
        require(config.plannedTrials <= BenchConfig.MAX_TRIALS)
        require(BenchMethod.SUMMARIES !in config.comparisons || config.order.backends.size == 1)
        require(config.syntheticImages || incoming.size == config.photoCount)
        require(config.workload != BenchWorkload.VISION || config.syntheticImages || truth != null)
        val model = identity()
        val generation = config.generation
        val header = BenchSessionHeader(
            UUID.randomUUID().toString(), Instant.now().toString(), BuildConfig.GIT_SHA, BuildConfig.VERSION_NAME,
            model, "${Build.MANUFACTURER} ${Build.MODEL} · ${Build.HARDWARE}",
            "Android ${Build.VERSION.RELEASE} · API ${Build.VERSION.SDK_INT}", "0.19.0",
            BenchRequestRecord(
                config.workload, config.order, generation.contextLength, generation.maxOutputTokens,
                generation.temperature, generation.topK, generation.topP, generation.thinking,
                generation.systemPrompt.isNotBlank(), config.warmUps, config.repeats, config.contextSweep,
                config.thinkingPair, config.needlePosition, config.visionCase.takeIf { config.workload == BenchWorkload.VISION },
                config.plannedTrials, config.photoCount, when {
                    config.photoCount == 0 -> "text"
                    config.syntheticImages -> "synthetic_image"
                    else -> "user_image"
                },
            ),
        )
        store.begin(header).use { journal ->
            val trials = mutableListOf<BenchTrial>()
            // The shared worker serializes all runs. Any leftover copies belong to a dead process.
            app.cacheDir.listFiles { file -> file.name.startsWith("roomtwin-benchmark-") && file.extension == "img" }
                ?.forEach { it.delete() }
            val photos = if (canContinue(control)) try {
                BenchPhotos(app, config, incoming)
            } catch (_: OutOfMemoryError) {
                control.stop(BenchEnd.OUT_OF_MEMORY)
                null
            } catch (_: Exception) {
                control.stop(BenchEnd.INPUT_PREPARATION_FAILED)
                null
            } else null
            val sampler = Executors.newSingleThreadScheduledExecutor()
            try {
                photos?.use {
                    conditions@ for (condition in config.conditions()) {
                        if (!canContinue(control)) break
                        // Cold means a fresh engine. The normal runtime cache remains in place.
                        engine.close()
                        val recipe = BenchRecipes.build(config, condition, truth)
                        val phases = listOf(BenchPhase.COLD) + List(config.warmUps) { BenchPhase.WARM_UP } +
                            List(config.repeats) { BenchPhase.WARM }
                        for (phase in phases) {
                            if (!canContinue(control)) break@conditions
                            if (identity() != model) {
                                control.stop(BenchEnd.MODEL_CHANGED)
                                break@conditions
                            }
                            val progress = BenchProgress(trials.size + 1, config.plannedTrials, condition, phase, sensors.sample())
                            onProgress(progress)
                            val peak = BenchPeak(progress.before)
                            val samples = sampler.scheduleWithFixedDelay({
                                if (sensors.isSevere()) control.stop(BenchEnd.THERMAL_LIMIT)
                                val sample = sensors.sample()
                                peak.add(sample)
                                onProgress(progress.copy(sample = sample))
                            }, 2, 2, TimeUnit.SECONDS)
                            val trial = try {
                                execute(engine, generation, progress, recipe, photos.images, control, peak)
                            } finally {
                                samples.cancel(false)
                            }
                            journal.trial(trial)
                            trials.add(trial)
                            onProgress(progress.copy(completed = trial))
                            if (trial.errorCode in unavailableEngineErrors) break
                        }
                    }
                }
            } finally {
                sampler.shutdownNow()
                if (control.reason != null || generation.contextLength == 8192) runCatching { engine.close() }
                else engine.resetPlayground()
            }
            val end = control.reason ?: BenchEnd.FINISHED
            journal.end(end)
            return BenchSession(header, trials, end)
        }
    }

    private fun execute(
        engine: GemmaRunner,
        generation: PlaygroundSettings,
        progress: BenchProgress,
        recipe: BenchRecipe,
        photos: List<Uri>,
        control: BenchControl,
        peak: BenchPeak,
    ): BenchTrial {
        val condition = progress.condition
        val settings = generation.copy(backend = condition.backend, contextLength = condition.contextLength, thinking = condition.thinking)
        val turns = mutableListOf<BenchTurn>()
        val capacity = if (condition.method == BenchMethod.DIRECT) 2 else 1
        var outcome = BenchOutcome.SUCCESS
        var code: String? = null
        var correct: Boolean? = null
        val started = TimeSource.Monotonic.markNow()
        fun request(prompt: BenchPrompt): String {
            if (!canContinue(control)) control.check()
            val callStarted = TimeSource.Monotonic.markNow()
            val result = try {
                engine.benchmarkTurn(settings, prompt.text, prompt.imageIndexes.map(photos::get), capacity, prompt.schema)
            } catch (error: Throwable) {
                turns.add(BenchTurn(turns.size + 1, prompt.text.length, prompt.imageIndexes.size,
                    callStarted.elapsedNow().inWholeMilliseconds, null, NativeTurnMetrics()))
                throw error
            }
            turns.add(BenchTurn(turns.size + 1, prompt.text.length, prompt.imageIndexes.size,
                result.reply.totalMillis, result.reply.firstVisibleMillis, result.native))
            if (result.reply.text.isBlank()) throw BenchOutputError("EMPTY_RESPONSE")
            return result.reply.text
        }
        try {
            engine.resetPlayground()
            val raw = if (condition.method == BenchMethod.SUMMARIES) {
                val question = recipe.turns.single()
                val observations = photos.indices.map { slot ->
                    engine.resetPlayground()
                    val label = if (slot == 0) "Before" else "After"
                    request(BenchPrompt(
                        "You see only the $label image. Describe this image's visible evidence relevant to the question. " +
                            "Report individual objects, counts or placement when relevant. Do not infer a change.\n" +
                            question.text.substringBeforeLast('\n'),
                        listOf(slot),
                    ))
                }
                engine.resetPlayground()
                request(BenchPrompt(BenchRecipes.summaryComparison(question.text, observations[0], observations[1]), schema = question.schema))
            } else {
                var answer = ""
                for (turn in recipe.turns) answer = request(turn)
                answer
            }
            if (recipe.answerIsJson && BenchGrading.jsonAnswer(raw) == null) {
                correct = false
                throw BenchOutputError("INVALID_JSON")
            }
            correct = recipe.expected?.let { BenchGrading.correct(raw, it, recipe.answerIsJson) }
        } catch (_: OutOfMemoryError) {
            outcome = BenchOutcome.ERROR
            code = "OUT_OF_MEMORY"
            control.stop(BenchEnd.OUT_OF_MEMORY)
        } catch (error: Exception) {
            val stopped = control.reason
            if (error is CancellationException && stopped != null) {
                outcome = BenchOutcome.INTERRUPTED
                code = stopped.name
            } else {
                outcome = BenchOutcome.ERROR
                code = classify(error)
                if (code == "OUT_OF_MEMORY") control.stop(BenchEnd.OUT_OF_MEMORY)
            }
        } catch (_: LinkageError) {
            outcome = BenchOutcome.ERROR
            code = "NATIVE_LINKAGE_FAILED"
        }
        val elapsed = started.elapsedNow().inWholeMilliseconds
        val after = sensors.sample()
        peak.add(after)
        val sampled = peak.read()
        if (sensors.isSevere()) control.stop(BenchEnd.THERMAL_LIMIT)
        return BenchTrial(progress.index, condition, progress.phase, outcome, code, elapsed, progress.before, after,
            sampled.pss, sampled.rss, sampled.thermal, turns, correct)
    }

    private fun canContinue(control: BenchControl): Boolean {
        if (sensors.isSevere()) control.stop(BenchEnd.THERMAL_LIMIT)
        return control.reason == null
    }

    private fun identity(): BenchModelIdentity {
        val name = "gemma-4-E2B-it.litertlm"
        val file = app.getExternalFilesDir(null)?.let { File(it, name) }?.takeIf { it.isFile }
        return BenchModelIdentity(name, file?.length(), file?.lastModified())
    }

    private fun classify(error: Exception): String {
        if (error is BenchOutputError) return error.code
        if (error !is GemmaRunner.Failure) return "REQUEST_FAILED"
        val details = generateSequence(error.cause) { it.cause?.takeUnless { cause -> cause === it } }
            .take(5).mapNotNull { it.message?.lowercase(Locale.ROOT) }.toList()
        return when {
            details.any { "out of memory" in it || "bad_alloc" in it } -> "OUT_OF_MEMORY"
            error.code == GemmaRunner.Error.INFERENCE_FAILED &&
                details.any { message -> contextErrors.any(message::contains) } -> "CONTEXT_LIMIT"
            else -> error.code.name
        }
    }

    private class BenchOutputError(val code: String) : Exception()

    private companion object {
        val unavailableEngineErrors = setOf("STORAGE_UNAVAILABLE", "MODEL_MISSING", "MODEL_INIT_FAILED",
            "NATIVE_LINKAGE_FAILED", "IMAGE_UNREADABLE")
        val contextErrors = listOf("context window", "context length", "context limit", "kv cache is full",
            "maximum number of tokens", "exceeds the maximum input", "exceeds maxnumtokens", "token capacity")
    }
}

private class BenchPeak(first: BenchResources) {
    data class Sample(val pss: Long?, val rss: Long?, val thermal: Int?)
    private var value = Sample(first.pssKiB, first.rssKiB, first.thermalStatus)

    @Synchronized
    fun add(resources: BenchResources) {
        value = Sample(
            maximum(value.pss, resources.pssKiB),
            maximum(value.rss, resources.rssKiB),
            maximum(value.thermal, resources.thermalStatus),
        )
    }

    @Synchronized
    fun read() = value

    private fun <T : Comparable<T>> maximum(first: T?, second: T?): T? = when {
        first == null -> second
        second == null -> first
        else -> maxOf(first, second)
    }
}
