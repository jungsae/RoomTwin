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

enum class InferenceBackend {
    CPU, GPU;

    internal fun runtimeBackend(): Backend = when (this) {
        CPU -> Backend.CPU()
        GPU -> Backend.GPU()
    }
}

data class GemmaInference(
    val rawResponse: String,
    val imageMillis: Long?,
    val initializationMillis: Long?,
    val inferenceMillis: Long,
    val backend: InferenceBackend,
    val decodeTokens: Int?,
)

// All calls are confined to the app's inference worker.
class GemmaRunner(private val context: Context, private val modelFile: File? = null) : AutoCloseable {
    private var engine: Engine? = null
    private var backend = InferenceBackend.CPU

    fun selectBackend(selected: InferenceBackend) {
        if (backend != selected) {
            close()
            backend = selected
        }
        Log.i(TAG, "selected=$backend")
    }

    fun initialize() {
        if (engine == null) initializeEngine()
    }

    fun analyzeRoom(
        image: Uri,
        prompt: String = RoomAnalysisJson.prompt,
        expectedMimeType: String? = null,
    ): GemmaInference = infer(
        prompt, image, maxOutputTokens = 768, expectedMimeType = expectedMimeType,
        responseFormat = ResponseFormat.json(RoomAnalysisJson.generationSchema),
    )

    fun infer(prompt: String, imageUri: Uri?): GemmaInference = infer(prompt, imageUri, maxOutputTokens = 256)

    @OptIn(ExperimentalApi::class)
    private fun infer(
        prompt: String,
        imageUri: Uri?,
        maxOutputTokens: Int,
        responseFormat: ResponseFormat? = null,
        expectedMimeType: String? = null,
    ): GemmaInference {
        val image = imageUri?.let { uri ->
            try {
                measureTimedValue { readImage(uri, expectedMimeType) }
            } catch (error: Exception) {
                throw Failure(Error.IMAGE_UNREADABLE, cause = error)
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
            throw Failure(Error.INFERENCE_FAILED, error.javaClass.simpleName, error)
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
        val model = modelFile ?: defaultModelFile()
        if (!model.isFile || !model.canRead() || model.length() == 0L) {
            throw Failure(Error.MODEL_MISSING, model.absolutePath)
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

    private fun defaultModelFile(): File {
        val directory = context.getExternalFilesDir(null) ?: throw Failure(Error.STORAGE_UNAVAILABLE)
        return File(directory, MODEL_NAME)
    }

    private fun initializationFailure(error: Throwable): Failure {
        Log.e(TAG, "initialize failed backend=$backend cause=${error.javaClass.simpleName}")
        return Failure(Error.MODEL_INIT_FAILED, "$backend · ${error.message ?: error.javaClass.simpleName}", error)
    }

    private fun readImage(uri: Uri, expectedMimeType: String?): ByteArray {
        val source = ImageDecoder.createSource(context.contentResolver, uri)
        val bitmap = ImageDecoder.decodeBitmap(source) { decoder, info, _ ->
            require(expectedMimeType == null || info.mimeType == expectedMimeType)
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
        activeEngine?.let {
            it.close()
            engine = null
            Log.i(TAG, "closed backend=$backend")
        }
    }

    enum class Error { STORAGE_UNAVAILABLE, MODEL_MISSING, MODEL_INIT_FAILED, IMAGE_UNREADABLE, INFERENCE_FAILED }

    class Failure(
        val code: Error,
        val detail: String? = null,
        cause: Throwable? = null,
    ) : Exception(cause)

    private companion object {
        const val TAG = "RoomTwinInference"
        const val MODEL_NAME = "gemma-4-E2B-it.litertlm"
        const val MAX_IMAGE_EDGE = 768
    }
}
