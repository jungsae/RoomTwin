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
import com.google.ai.edge.litertlm.ThinkingConfig
import java.io.ByteArrayOutputStream
import java.io.File

// All calls are confined to the app's inference worker.
internal class GemmaRunner(private val context: Context) : AutoCloseable {
    private var engine: Engine? = null

    fun infer(prompt: String, imageUri: Uri?): String {
        val image = imageUri?.let { uri ->
            try {
                readImage(uri)
            } catch (error: Exception) {
                throw Failure(R.string.image_failed, cause = error)
            }
        }
        val activeEngine = getOrInitializeEngine()
        val input = buildList {
            image?.let { add(Content.ImageBytes(it)) }
            add(Content.Text(prompt))
        }
        return try {
            activeEngine.createConversation(
                ConversationConfig(
                    maxOutputToken = 256,
                    thinkingConfig = ThinkingConfig(enableThinking = false),
                ),
            ).use { conversation ->
                conversation.sendMessage(Contents.of(input)).toString().trim()
            }
        } catch (error: Exception) {
            throw Failure(R.string.inference_failed, error.javaClass.simpleName, error)
        }
    }

    private fun getOrInitializeEngine(): Engine {
        engine?.let { return it }

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
