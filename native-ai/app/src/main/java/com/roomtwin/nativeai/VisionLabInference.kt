package com.roomtwin.nativeai

import android.net.Uri
import kotlin.time.TimeSource

internal data class LabRequest(
    val feature: LabFeature,
    val settings: PlaygroundSettings,
    val images: List<Uri>,
    val text: String,
    val followUp: Boolean,
    val difficulty: QuizDifficulty,
    val language: String,
    val compareSummaries: Boolean,
)

internal enum class LabComparison { DIRECT, SUMMARIES }
internal data class LabReply(val inference: VisionInference, val comparison: LabComparison? = null)

internal fun GemmaRunner.runVisionLab(request: LabRequest, onText: (String) -> Unit): LabReply {
    val (feature, settings, images, text, followUp, difficulty, language, compareSummaries) = request
    require(images.size == if (followUp) 0 else feature.imageCount)
    return when (feature) {
        LabFeature.DETECTIVE -> LabReply(streamVisionLab(
            settings, feature.name, VisionLabPrompts.detective(text.takeIf { followUp }), images,
            keepHistory = true, onText = onText,
        ))
        LabFeature.QUIZ -> LabReply(streamVisionLab(
            settings, feature.name, VisionLabPrompts.quiz(difficulty), images, VisionLabSchemas.quiz,
        ))
        LabFeature.FIND -> LabReply(streamVisionLab(
            settings, feature.name, VisionLabPrompts.find(text), images, VisionLabSchemas.find,
        ))
        LabFeature.TRANSLATE -> LabReply(streamVisionLab(
            settings, feature.name, VisionLabPrompts.translate(language), images, VisionLabSchemas.translation,
        ))
        LabFeature.SPACE -> LabReply(streamVisionLab(settings, feature.name, VisionLabPrompts.space, images, onText = onText))
        LabFeature.CHANGES -> compareLabPhotos(settings, images, compareSummaries, onText)
    }
}

private fun GemmaRunner.compareLabPhotos(
    settings: PlaygroundSettings,
    images: List<Uri>,
    compareSummaries: Boolean,
    onText: (String) -> Unit,
): LabReply {
    val started = TimeSource.Monotonic.markNow()
    if (!compareSummaries) {
        try {
            val result = streamVisionLab(settings, "changes", VisionLabPrompts.changes, images)
            if (result.text.isNotBlank()) return LabReply(result, LabComparison.DIRECT)
        } catch (failure: GemmaRunner.Failure) {
            // A failed two-image configuration gets one try through separate single-image sessions.
            if (failure.code != GemmaRunner.Error.MODEL_INIT_FAILED && failure.code != GemmaRunner.Error.INFERENCE_FAILED) {
                throw failure
            }
        }
    }

    val inventoryConfig = settings.copy(systemPrompt = "", thinking = false, maxOutputTokens = minOf(224, settings.maxOutputTokens))
    val descriptions = images.mapIndexed { index, image ->
        streamVisionLab(inventoryConfig, "inventory-$index", VisionLabPrompts.inventory, listOf(image)).text
    }
    if (descriptions.any { it.isBlank() }) throw GemmaRunner.Failure(GemmaRunner.Error.INFERENCE_FAILED, "Vision summary empty")
    val finalPassStarted = started.elapsedNow().inWholeMilliseconds
    val result = streamVisionLab(
        settings, "summary-comparison", VisionLabPrompts.compareSummaries(descriptions[0], descriptions[1]), emptyList(),
        onText = onText,
    )
    return LabReply(result.copy(
        totalMillis = started.elapsedNow().inWholeMilliseconds,
        firstVisibleMillis = result.firstVisibleMillis?.plus(finalPassStarted),
    ), LabComparison.SUMMARIES)
}
