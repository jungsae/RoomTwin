package com.roomtwin.nativeai

import android.content.Context
import android.graphics.Bitmap
import android.graphics.ImageDecoder
import android.net.Uri
import android.util.Log
import com.google.ai.edge.litertlm.Backend
import com.google.ai.edge.litertlm.Content
import com.google.ai.edge.litertlm.Contents
import com.google.ai.edge.litertlm.Conversation
import com.google.ai.edge.litertlm.ConversationConfig
import com.google.ai.edge.litertlm.Engine
import com.google.ai.edge.litertlm.EngineConfig
import com.google.ai.edge.litertlm.ExperimentalApi
import com.google.ai.edge.litertlm.ExperimentalFlags
import com.google.ai.edge.litertlm.Message
import com.google.ai.edge.litertlm.MessageCallback
import com.google.ai.edge.litertlm.ResponseFormat
import com.google.ai.edge.litertlm.SamplerConfig
import com.google.ai.edge.litertlm.ThinkingConfig
import java.io.ByteArrayOutputStream
import java.io.File
import java.util.concurrent.CompletableFuture
import java.util.concurrent.CompletionException
import kotlin.time.TimeSource
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

data class VisionInference(
    val text: String,
    val totalMillis: Long,
    val firstVisibleMillis: Long?,
    val decodeTokens: Int?,
    val decodeTokensPerSecond: Double?,
    val backend: InferenceBackend,
)

// All calls are confined to the app's inference worker.
class GemmaRunner(private val context: Context, private val modelFile: File? = null) : AutoCloseable {
    private var engine: Engine? = null
    private var backend = InferenceBackend.CPU
    private var contextLength = PlaygroundSettings.DEFAULT_CONTEXT_LENGTH
    private var chat: Conversation? = null
    private var chatSettings: PlaygroundSettings? = null
    private var chatKey: String? = null
    private var chatSchema: String? = null
    private var imageCapacity = 1
    private var visionMetrics = false

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

    // Continuous Playground and Lab conversations both use this engine, with separate keys.
    fun chat(
        settings: PlaygroundSettings,
        question: String,
        imageUri: Uri?,
        onText: (String) -> Unit,
    ) {
        require(question.isNotBlank() && question.length <= PlaygroundSettings.MAX_QUESTION_LENGTH)
        stream(settings, "playground", question, listOfNotNull(imageUri), null, false, onText)
    }

    fun streamVisionLab(
        settings: PlaygroundSettings,
        feature: String,
        question: String,
        images: List<Uri>,
        schema: String? = null,
        keepHistory: Boolean = false,
        onText: (String) -> Unit = {},
    ): VisionInference {
        if (!keepHistory) resetPlayground()
        try {
            return stream(settings, "vision:$feature", question, images, schema, true, onText)
        } finally {
            if (!keepHistory) resetPlayground()
        }
    }

    @OptIn(ExperimentalApi::class)
    private fun stream(
        settings: PlaygroundSettings,
        key: String,
        question: String,
        images: List<Uri>,
        schema: String?,
        metrics: Boolean,
        onText: (String) -> Unit,
    ): VisionInference {
        require(question.isNotBlank() && images.size <= 2)
        val started = TimeSource.Monotonic.markNow()
        val input = buildList {
            for (uri in images) {
                try {
                    add(Content.ImageBytes(readImage(uri, expectedMimeType = null)))
                } catch (error: Exception) {
                    throw Failure(Error.IMAGE_UNREADABLE, cause = error)
                }
            }
            add(Content.Text(question))
        }
        selectBackend(settings.backend)
        configureEngine(settings.contextLength, images.size.coerceAtLeast(1), metrics)
        if (chatSettings != settings || chatKey != key || chatSchema != schema) resetPlayground()
        val activeEngine = engine ?: initializeEngine()
        try {
            val conversation = chat ?: activeEngine.createConversation(
                ConversationConfig(
                    systemInstruction = settings.systemPrompt.takeIf { it.isNotBlank() }?.let { Contents.of(it) },
                    samplerConfig = SamplerConfig(settings.topK, settings.topP, settings.temperature),
                    automaticToolCalling = false,
                    maxOutputToken = settings.maxOutputTokens,
                    thinkingConfig = ThinkingConfig(enableThinking = settings.thinking),
                    enableResponseFormat = schema != null,
                ),
            ).also {
                chat = it
                chatSettings = settings
                chatKey = key
                chatSchema = schema
            }
            val finished = CompletableFuture<Unit>()
            val response = StringBuilder()
            var firstVisibleMillis: Long? = null
            conversation.sendMessageAsync(
                Contents.of(input),
                object : MessageCallback {
                    override fun onMessage(message: Message) {
                        for (content in message.contents.contents) {
                            if (content !is Content.Text || content.text.isEmpty()) continue
                            if (firstVisibleMillis == null && content.text.isNotBlank()) {
                                firstVisibleMillis = started.elapsedNow().inWholeMilliseconds
                            }
                            response.append(content.text)
                            onText(content.text)
                        }
                    }

                    override fun onDone() {
                        finished.complete(Unit)
                    }

                    override fun onError(throwable: Throwable) {
                        finished.completeExceptionally(throwable)
                    }
                },
                responseFormat = schema?.let { ResponseFormat.json(it) },
            )
            // The app owns one worker. Close or reuse the engine only after the terminal callback.
            try {
                finished.join()
            } catch (error: CompletionException) {
                throw error.cause ?: error
            }
            val elapsed = started.elapsedNow().inWholeMilliseconds
            val benchmark = if (metrics) runCatching { conversation.getBenchmarkInfo() }.getOrNull() else null
            return VisionInference(
                response.toString(),
                elapsed,
                firstVisibleMillis,
                benchmark?.lastDecodeTokenCount?.takeIf { it > 0 },
                benchmark?.lastDecodeTokensPerSecond?.takeIf { it.isFinite() && it > 0 },
                backend,
            )
        } catch (error: Exception) {
            runCatching { resetPlayground() }
            throw Failure(Error.INFERENCE_FAILED, error.javaClass.simpleName, error)
        }
    }

    fun resetPlayground() {
        val current = chat
        chat = null
        chatSettings = null
        chatKey = null
        chatSchema = null
        current?.close()
    }

    private fun configureEngine(tokens: Int, images: Int = 1, metrics: Boolean = false) {
        if (contextLength != tokens || imageCapacity != images || visionMetrics != metrics) {
            close()
            contextLength = tokens
            imageCapacity = images
            visionMetrics = metrics
        }
    }

    @OptIn(ExperimentalApi::class)
    private fun infer(
        prompt: String,
        imageUri: Uri?,
        maxOutputTokens: Int,
        responseFormat: ResponseFormat? = null,
        expectedMimeType: String? = null,
    ): GemmaInference {
        resetPlayground()
        configureEngine(PlaygroundSettings.DEFAULT_CONTEXT_LENGTH)
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

    @OptIn(ExperimentalApi::class)
    private fun initializeEngine(): Engine {
        val model = modelFile ?: defaultModelFile()
        if (!model.isFile || !model.canRead() || model.length() == 0L) {
            throw Failure(Error.MODEL_MISSING, model.absolutePath)
        }

        val previousBenchmarkFlag = ExperimentalFlags.enableBenchmark
        val initialized = try {
            if (visionMetrics) ExperimentalFlags.enableBenchmark = true
            Engine(
                EngineConfig(
                    modelPath = model.absolutePath,
                    backend = backend.runtimeBackend(),
                    visionBackend = backend.runtimeBackend(),
                    maxNumTokens = contextLength,
                    maxNumImages = imageCapacity,
                    cacheDir = File(context.cacheDir, cacheName()).apply { mkdirs() }.absolutePath,
                ),
            ).apply { initialize() }
        } catch (error: Exception) {
            throw initializationFailure(error)
        } catch (error: LinkageError) {
            throw initializationFailure(error)
        } finally {
            if (visionMetrics) ExperimentalFlags.enableBenchmark = previousBenchmarkFlag
        }
        Log.i(TAG, "initialized backend=$backend vision=$backend")
        engine = initialized
        return initialized
    }

    private fun cacheName(): String = when {
        imageCapacity > 1 -> "${backend.name}-$contextLength-images$imageCapacity"
        contextLength == PlaygroundSettings.DEFAULT_CONTEXT_LENGTH -> backend.name
        else -> "${backend.name}-$contextLength"
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
        resetPlayground()
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
