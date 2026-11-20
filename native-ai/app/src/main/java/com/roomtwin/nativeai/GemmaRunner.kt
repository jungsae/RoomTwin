package com.roomtwin.nativeai

import android.content.Context
import android.graphics.Bitmap
import android.graphics.ImageDecoder
import android.net.Uri
import android.util.Log
import com.google.ai.edge.litertlm.Backend
import com.google.ai.edge.litertlm.Content
import com.google.ai.edge.litertlm.Contents
import com.google.ai.edge.litertlm.ConversationConfig
import com.google.ai.edge.litertlm.Engine
import com.google.ai.edge.litertlm.EngineConfig
import com.google.ai.edge.litertlm.ExperimentalApi
import com.google.ai.edge.litertlm.ExperimentalFlags
import com.google.ai.edge.litertlm.ResponseFormat
import com.google.ai.edge.litertlm.ThinkingConfig
import java.io.ByteArrayOutputStream
import java.io.File
import kotlin.time.measureTimedValue

internal enum class InferenceBackend {
    CPU, GPU;

    fun runtimeBackend(): Backend = when (this) {
        CPU -> Backend.CPU()
        GPU -> Backend.GPU()
    }
}

internal data class GemmaInference(
    val rawResponse: String,
    val imageMillis: Long?,
    val initializationMillis: Long?,
    val inferenceMillis: Long,
    val backend: InferenceBackend,
    val decodeTokens: Int?,
)

// All calls are confined to the app's inference worker.
internal class GemmaRunner(private val context: Context) : AutoCloseable {
    private var engine: Engine? = null
    private var backend = InferenceBackend.CPU

    fun selectBackend(selected: InferenceBackend) {
        if (backend != selected) {
            close()
            backend = selected
        }
        Log.i(TAG, "selected=$backend")
    }

    fun analyzeRoom(image: Uri): GemmaInference = infer(
        RoomAnalysisJson.prompt, image, maxOutputTokens = 768,
        responseFormat = ResponseFormat.json(RoomAnalysisJson.generationSchema),
    )

    @OptIn(ExperimentalApi::class)
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
        var decodeTokens: Int? = null
        val generation = try {
            measureTimedValue {
                activeEngine.createConversation(
                    ConversationConfig(
                        maxOutputToken = maxOutputTokens,
                        enableResponseFormat = responseFormat != null,
                        thinkingConfig = ThinkingConfig(enableThinking = false),
                    ),
                ).use { conversation ->
                    val reply = conversation.sendMessage(Contents.of(input), responseFormat = responseFormat)
                    if (ExperimentalFlags.enableBenchmark) {
                        decodeTokens = conversation.getBenchmarkInfo().lastDecodeTokenCount
                    }
                    reply.toString()
                }
            }
        } catch (error: Exception) {
            throw Failure(R.string.inference_failed, error.javaClass.simpleName, error)
        }
        Log.i(TAG, "completed backend=$backend")
        return GemmaInference(
            generation.value,
            image?.duration?.inWholeMilliseconds,
            initialization?.duration?.inWholeMilliseconds,
            generation.duration.inWholeMilliseconds,
            backend, decodeTokens,
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
                    backend = backend.runtimeBackend(),
                    visionBackend = backend.runtimeBackend(),
                    maxNumTokens = 2048,
                    maxNumImages = 1,
                    cacheDir = File(context.cacheDir, backend.name).apply { mkdirs() }.absolutePath,
                ),
            ).apply { initialize() }
        } catch (error: Exception) {
            throw initializationFailure(error)
        } catch (error: LinkageError) {
            throw initializationFailure(error)
        }
        Log.i(TAG, "initialized backend=$backend vision=$backend")
        engine = initialized
        return initialized
    }

    private fun initializationFailure(error: Throwable): Failure {
        Log.e(TAG, "initialize failed backend=$backend cause=${error.javaClass.simpleName}")
        return Failure(R.string.model_failed, "$backend · ${error.message ?: error.javaClass.simpleName}", error)
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
        activeEngine?.let {
            it.close()
            Log.i(TAG, "closed backend=$backend")
        }
    }

    internal class Failure(
        val messageId: Int,
        val detail: String? = null,
        cause: Throwable? = null,
    ) : Exception(cause)

    private companion object {
        const val TAG = "RoomTwinInference"
        const val MODEL_NAME = "gemma-4-E2B-it.litertlm"
        const val MAX_IMAGE_EDGE = 768
    }
}
