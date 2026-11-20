package com.roomtwin.nativeai

import android.content.Context
import android.graphics.Bitmap
import android.graphics.ImageDecoder
import android.net.Uri
import com.google.ai.edge.litertlm.Backend
import com.google.ai.edge.litertlm.Content
import com.google.ai.edge.litertlm.Contents
import com.google.ai.edge.litertlm.ConversationConfig
import com.google.ai.edge.litertlm.Engine
import com.google.ai.edge.litertlm.EngineConfig
import com.google.ai.edge.litertlm.ResponseFormat
import com.google.ai.edge.litertlm.ThinkingConfig
import java.io.ByteArrayOutputStream
import java.io.File
import kotlin.time.measureTimedValue

internal data class GemmaInference(
    val rawResponse: String,
    val imageMillis: Long?,
    val initializationMillis: Long?,
    val inferenceMillis: Long,
)

// All calls are confined to the app's inference worker.
internal class GemmaRunner(private val context: Context) : AutoCloseable {
    private var engine: Engine? = null

    fun infer(
        prompt: String,
        imageUri: Uri?,
        maxOutputTokens: Int = 256,
        responseFormat: ResponseFormat? = null,
    ): GemmaInference {
        val image = imageUri?.let { uri ->
            try {
                measureTimedValue { readImage(uri) }
            } catch (error: Exception) {
                throw Failure(R.string.image_failed, cause = error)
            }
        }
        val existingEngine = engine
        val initialization = if (existingEngine == null) {
            measureTimedValue { initializeEngine() }
        } else {
            null
        }
        val activeEngine = existingEngine ?: checkNotNull(initialization).value
        val input = buildList {
            image?.let { add(Content.ImageBytes(it.value)) }
            add(Content.Text(prompt))
        }
        val generation = try {
            measureTimedValue {
                activeEngine.createConversation(
                    ConversationConfig(
                        maxOutputToken = maxOutputTokens,
                        enableResponseFormat = responseFormat != null,
                        thinkingConfig = ThinkingConfig(enableThinking = false),
                    ),
                ).use { conversation ->
                    conversation.sendMessage(Contents.of(input), responseFormat = responseFormat).toString()
                }
            }
        } catch (error: Exception) {
            throw Failure(R.string.inference_failed, error.javaClass.simpleName, error)
        }
        return GemmaInference(
            generation.value,
            image?.duration?.inWholeMilliseconds,
            initialization?.duration?.inWholeMilliseconds,
            generation.duration.inWholeMilliseconds,
        )
    }

    private fun initializeEngine(): Engine {
        val modelDirectory = context.getExternalFilesDir(null)
            ?: throw Failure(R.string.storage_unavailable)
        val model = File(modelDirectory, MODEL_NAME)
        if (!model.isFile || !model.canRead() || model.length() == 0L) {
            throw Failure(R.string.model_missing, model.absolutePath)
        }

        val initialized = try {
            Engine(
                EngineConfig(
                    modelPath = model.absolutePath,
                    backend = Backend.CPU(),
                    visionBackend = Backend.CPU(),
                    maxNumTokens = 2048,
                    maxNumImages = 1,
                    cacheDir = context.cacheDir.absolutePath,
                ),
            ).apply { initialize() }
        } catch (error: Exception) {
            throw Failure(R.string.model_failed, error.javaClass.simpleName, error)
        } catch (error: LinkageError) {
            throw Failure(R.string.model_failed, error.javaClass.simpleName, error)
        }
        engine = initialized
        return initialized
    }

    private fun readImage(uri: Uri): ByteArray {
        val source = ImageDecoder.createSource(context.contentResolver, uri)
        val bitmap = ImageDecoder.decodeBitmap(source) { decoder, info, _ ->
            decoder.allocator = ImageDecoder.ALLOCATOR_SOFTWARE
            val width = info.size.width
            val height = info.size.height
            val longestEdge = maxOf(width, height)
            if (longestEdge > MAX_IMAGE_EDGE) {
                val scale = MAX_IMAGE_EDGE.toDouble() / longestEdge
                decoder.setTargetSize(
                    (width * scale).toInt().coerceAtLeast(1),
                    (height * scale).toInt().coerceAtLeast(1),
                )
            }
        }
        try {
            val jpeg = ByteArrayOutputStream()
            check(bitmap.compress(Bitmap.CompressFormat.JPEG, 90, jpeg))
            return jpeg.toByteArray()
        } finally {
            bitmap.recycle()
        }
    }

    override fun close() {
        val activeEngine = engine
        engine = null
        activeEngine?.close()
    }

    internal class Failure(
        val messageId: Int,
        val detail: String? = null,
        cause: Throwable? = null,
    ) : Exception(cause)

    private companion object {
        const val MODEL_NAME = "gemma-4-E2B-it.litertlm"
        const val MAX_IMAGE_EDGE = 768
    }
}
