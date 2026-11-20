package com.roomtwin.nativeai

import kotlin.math.ceil
import kotlin.math.sqrt

internal enum class BenchWorkload(val title: String, val imageCount: Int = 0) {
    TEXT("Text Generation"), IMAGE("Single Image Understanding", 1), TWO_IMAGES("Multi-image Understanding", 2),
    MULTI_TURN("Multi-turn Conversation"), LONG_CONTEXT("Long Context Processing"), JSON("Structured JSON Generation"),
    NEEDLE("Needle-in-a-Haystack"), RECALL("Multi-turn Recall"), CONFLICT("Context Conflict"),
    SATURATION("Context Saturation"), VISION("Vision Evaluation"),
}

internal enum class BenchOrder(val title: String, val backends: List<InferenceBackend>) {
    CPU("CPU", listOf(InferenceBackend.CPU)), GPU("GPU", listOf(InferenceBackend.GPU)),
    CPU_GPU("CPU → GPU", listOf(InferenceBackend.CPU, InferenceBackend.GPU)),
    GPU_CPU("GPU → CPU", listOf(InferenceBackend.GPU, InferenceBackend.CPU)),
}

internal enum class NeedlePosition(val label: String) { FRONT("앞"), MIDDLE("중간"), END("뒤") }

internal enum class VisionCase(val title: String, val photos: Int, val prompt: String, val expected: String) {
    PRESENT("객체 존재", 1, "Is a blue square visible? Answer yes or no.", "yes"),
    ABSENT("객체 부재", 1, "Is a red triangle visible? Answer yes or no.", "no"),
    COUNT("물체 개수", 1, "How many separate blue squares are visible? Answer with a number only.", "2"),
    RELATION("상대 위치", 1, "Is the yellow circle left or right of both blue squares? Answer left or right.", "right"),
    CHANGE("Before/After 변화", 2, "Before is the first image. How many blue squares were added in After? Answer with a number.", "1"),
    METHOD("직접 비교와 요약 비교", 2, "Before is the first image. How many blue squares were added in After? Answer with a number.", "1"),
    SURFACE("벽과 가구 표면", 1, "Is the wall behind the brown cabinet gray or brown? Answer gray or brown.", "gray"),
    OCR("이미지 문자 판독", 1, "Transcribe the large centered line, preserving digits and spacing.", "LAB 2048"),
}

internal enum class BenchMethod { STANDARD, DIRECT, SUMMARIES }
internal enum class BenchPhase { COLD, WARM_UP, WARM }
internal enum class BenchOutcome { SUCCESS, ERROR, INTERRUPTED }
internal enum class BenchEnd { FINISHED, STOP_REQUESTED, THERMAL_LIMIT, OUT_OF_MEMORY, INPUT_PREPARATION_FAILED, MODEL_CHANGED, INCOMPLETE }

internal data class BenchCondition(
    val backend: InferenceBackend,
    val contextLength: Int,
    val thinking: Boolean,
    val loadPercent: Int,
    val method: BenchMethod,
)

internal data class BenchConfig(
    val workload: BenchWorkload,
    val order: BenchOrder,
    val generation: PlaygroundSettings,
    val warmUps: Int,
    val repeats: Int,
    val contextSweep: Boolean = false,
    val thinkingPair: Boolean = false,
    val needlePosition: NeedlePosition = NeedlePosition.MIDDLE,
    val visionCase: VisionCase = VisionCase.PRESENT,
    val syntheticImages: Boolean = true,
) {
    init {
        require(warmUps in 0..3 && repeats in 1..10)
    }

    val photoCount: Int get() = if (workload == BenchWorkload.VISION) visionCase.photos else workload.imageCount
    val comparisons: List<BenchMethod>
        get() = when {
            workload == BenchWorkload.VISION && visionCase == VisionCase.METHOD -> listOf(BenchMethod.DIRECT, BenchMethod.SUMMARIES)
            photoCount == 2 -> listOf(BenchMethod.DIRECT)
            else -> listOf(BenchMethod.STANDARD)
        }
    val needsMobileConfirmation: Boolean get() = generation.contextLength == 8192 ||
        (photoCount == 2 && plannedTrials > 4)

    val plannedTrials: Int get() = conditions().size * (1 + warmUps + repeats)

    companion object {
        const val MAX_TRIALS = 120
    }

    fun conditions(): List<BenchCondition> = buildList {
        val contexts = if (contextSweep) PlaygroundSettings.CONTEXT_LENGTHS.take(if (generation.contextLength == 8192) 3 else 2)
            else listOf(generation.contextLength)
        val thinkingValues = if (thinkingPair) listOf(false, true) else listOf(generation.thinking)
        val loads = if (workload == BenchWorkload.SATURATION) listOf(30, 60, 90, 110) else listOf(0)
        for (backend in order.backends) for (context in contexts) for (thinking in thinkingValues) {
            for (method in comparisons) for (load in loads) add(BenchCondition(backend, context, thinking, load, method))
        }
    }
}

internal data class BenchResources(
    val pssKiB: Long?,
    val rssKiB: Long?,
    val nativeHeapBytes: Long?,
    val javaHeapBytes: Long?,
    val availableMemoryBytes: Long?,
    val thermalStatus: Int?,
    val batteryCelsius: Double?,
    val batteryPercent: Int?,
    val gpuDedicatedMemoryBytes: Long? = null,
)

internal data class BenchTurn(
    val turn: Int,
    val inputCharacters: Int,
    val imageCount: Int,
    val latencyMillis: Long,
    val firstChunkMillis: Long?,
    val native: NativeTurnMetrics,
    val actualTtftMillis: Long? = null,
)

internal data class BenchTrial(
    val index: Int,
    val condition: BenchCondition,
    val phase: BenchPhase,
    val outcome: BenchOutcome,
    val errorCode: String?,
    val latencyMillis: Long,
    val before: BenchResources,
    val after: BenchResources,
    val peakSampledPssKiB: Long?,
    val peakSampledRssKiB: Long?,
    val peakThermalStatus: Int?,
    val turns: List<BenchTurn>,
    val evaluationCorrect: Boolean?,
) {
    val outputTokens: Int?
        get() = turns.map { it.native.outputTokens }.let { sizes ->
            if (sizes.isEmpty() || sizes.any { it == null }) null else sizes.filterNotNull().sum()
        }
}

internal data class BenchStats(
    val count: Int,
    val mean: Double,
    val median: Double,
    val p95: Long,
    val standardDeviation: Double,
    val minimum: Long,
    val maximum: Long,
) {
    companion object {
        fun of(values: List<Long>): BenchStats? {
            if (values.isEmpty()) return null
            val ordered = values.sorted()
            val count = ordered.size
            val center = count / 2
            val median = if (count % 2 == 1) ordered[center].toDouble()
                else (ordered[center - 1].toDouble() + ordered[center]) / 2
            val mean = ordered.average()
            val variance = ordered.sumOf { (it - mean) * (it - mean) } / count
            return BenchStats(count, mean, median, ordered[ceil(count * .95).toInt() - 1],
                sqrt(variance), ordered.first(), ordered.last())
        }
    }
}
