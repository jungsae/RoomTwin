package com.roomtwin.nativeai.bridge

import android.content.Context
import android.net.Uri
import com.roomtwin.nativeai.GemmaRunner
import com.roomtwin.nativeai.InferenceBackend
import com.roomtwin.nativeai.RoomAnalysisJson
import com.roomtwin.nativeai.RoomAnalysisParseResult
import java.io.File
import java.io.IOException
import java.util.concurrent.Executors

/** Process-wide bridge accessed by Unity via its JVM name. The host owns the session. */
class RoomTwinAiBridge private constructor(context: Context) {
    private val session = BridgeSession(
        Executors.newSingleThreadExecutor { runnable -> Thread(runnable, "RoomTwinAI") },
    ) { config -> AndroidRoomEngine(context, config) }

    fun initialize(modelPath: String, backend: String): String {
        val selected = InferenceBackend.entries.find { it.name == backend }
        if (selected == null || !modelPath.startsWith("/") || modelPath.length > 4096) {
            return BridgeProtocol.error("INVALID_REQUEST")
        }
        return session.initialize(BridgeConfig(modelPath, selected))
    }

    fun analyzeImageAsync(requestJson: String): String = try {
        session.analyze(BridgeProtocol.readRequest(requestJson))
    } catch (error: BridgeProtocol.RequestError) {
        BridgeProtocol.error(error.code, error.id)
    }

    fun getStatus(): String = session.getStatus()

    fun takeResult(requestId: String): String = if (BridgeProtocol.validId(requestId)) {
        session.takeResult(requestId)
    } else {
        BridgeProtocol.error("INVALID_REQUEST")
    }

    fun cancel(requestId: String): String = if (BridgeProtocol.validId(requestId)) {
        session.cancel(requestId)
    } else {
        BridgeProtocol.error("INVALID_REQUEST")
    }

    fun release(): String = session.release()

    companion object {
        private var instance: RoomTwinAiBridge? = null

        @JvmStatic
        @Synchronized
        fun getInstance(context: Context): RoomTwinAiBridge {
            val current = instance
            if (current != null && !current.session.isReleased()) return current
            return RoomTwinAiBridge(context.applicationContext).also { instance = it }
        }
    }
}

private class AndroidRoomEngine(private val context: Context, private val config: BridgeConfig) : RoomEngine {
    private var runner: GemmaRunner? = null

    override fun initialize() {
        val model = appFile(config.modelPath, GemmaRunner.Error.MODEL_MISSING)
        val configured = GemmaRunner(context, model)
        runner = configured
        configured.selectBackend(config.backend)
        configured.initialize()
    }

    override fun analyze(request: ImageRequest): RoomAnalysisParseResult {
        val uri = Uri.parse(request.uri)
        val localUri = when (uri.scheme) {
            "file" -> Uri.fromFile(appFile(uri.path, GemmaRunner.Error.IMAGE_UNREADABLE))
            "content" -> uri.takeIf { !it.authority.isNullOrEmpty() }
                ?: throw GemmaRunner.Failure(GemmaRunner.Error.IMAGE_UNREADABLE)
            else -> throw GemmaRunner.Failure(GemmaRunner.Error.IMAGE_UNREADABLE)
        }
        val result = checkNotNull(runner).analyzeRoom(localUri, expectedMimeType = request.mimeType)
        return RoomAnalysisJson.parse(result.rawResponse)
    }

    override fun close() {
        runner?.close()
        runner = null
    }

    private fun appFile(path: String?, error: GemmaRunner.Error): File {
        if (path.isNullOrBlank() || !path.startsWith("/")) throw GemmaRunner.Failure(error)
        try {
            val file = File(path).canonicalFile
            val roots = listOfNotNull(
                context.filesDir, context.cacheDir, context.getExternalFilesDir(null), context.externalCacheDir,
            )
            if (roots.any { file.path.startsWith(it.canonicalPath + File.separator) }) return file
        } catch (cause: IOException) {
            throw GemmaRunner.Failure(error, cause = cause)
        } catch (cause: SecurityException) {
            throw GemmaRunner.Failure(error, cause = cause)
        }
        throw GemmaRunner.Failure(error)
    }
}
