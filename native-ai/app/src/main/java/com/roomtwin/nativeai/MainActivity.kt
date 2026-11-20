package com.roomtwin.nativeai

import android.app.Activity
import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.view.View
import android.view.WindowInsets
import android.widget.Button
import android.widget.EditText
import android.widget.RadioGroup
import android.widget.TextView
import java.util.concurrent.Executors
import kotlin.time.measureTimedValue

class MainActivity : Activity() {
    private lateinit var runner: GemmaRunner
    private lateinit var backends: RadioGroup
    private lateinit var prompt: EditText
    private lateinit var pickImage: Button
    private lateinit var clearImage: Button
    private lateinit var runInference: Button
    private lateinit var analyzeRoom: Button
    private lateinit var status: TextView
    private lateinit var timings: TextView
    private lateinit var rawHeader: TextView
    private lateinit var validatedHeader: TextView
    private lateinit var validatedJson: TextView
    private lateinit var response: TextView
    private var selectedImage: Uri? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        applySystemInsets(findViewById(R.id.screen))

        runner = GemmaRunner(applicationContext)
        prompt = findViewById(R.id.prompt)
        pickImage = findViewById(R.id.pick_image)
        clearImage = findViewById(R.id.clear_image)
        runInference = findViewById(R.id.run_inference)
        analyzeRoom = findViewById(R.id.analyze_room)
        status = findViewById(R.id.status)
        timings = findViewById(R.id.timings)
        rawHeader = findViewById(R.id.raw_header)
        validatedHeader = findViewById(R.id.validated_header)
        validatedJson = findViewById(R.id.validated_json)
        response = findViewById(R.id.response)

        backends = findViewById(R.id.backends)
        backends.setOnCheckedChangeListener { _, id ->
            val selected = if (id == R.id.gpu) InferenceBackend.GPU else InferenceBackend.CPU
            worker.execute { runner.selectBackend(selected) }
            status.setText(R.string.ready)
            timings.visibility = View.GONE
            response.text = ""
            rawHeader.visibility = View.GONE
            validatedHeader.visibility = View.GONE
            validatedJson.visibility = View.GONE
        }
        pickImage.setOnClickListener { openImagePicker() }
        clearImage.setOnClickListener { setSelectedImage(null) }
        runInference.setOnClickListener { submit(analysisMode = false) }
        analyzeRoom.setOnClickListener { submit(analysisMode = true) }
    }

    @Suppress("DEPRECATION")
    private fun openImagePicker() {
        val picker = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = "image/*"
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        try {
            startActivityForResult(picker, REQUEST_IMAGE)
        } catch (_: ActivityNotFoundException) {
            status.setText(R.string.no_picker)
        }
    }

    @Deprecated("Handles the platform file picker result.")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != REQUEST_IMAGE) return
        val image = data?.data
        when {
            resultCode != RESULT_OK -> status.setText(R.string.image_cancelled)
            image == null -> status.setText(R.string.image_failed)
            else -> {
                setSelectedImage(image)
                status.setText(R.string.ready)
            }
        }
    }

    private fun setSelectedImage(uri: Uri?) {
        selectedImage = uri
        pickImage.setText(if (uri == null) R.string.pick_image else R.string.change_image)
        clearImage.visibility = if (uri == null) View.GONE else View.VISIBLE
    }

    private fun submit(analysisMode: Boolean) {
        val image = selectedImage
        if (analysisMode && image == null) {
            status.setText(R.string.room_image_required)
            return
        }
        val input = prompt.text.toString().trim()
        if (!analysisMode && input.isEmpty()) {
            prompt.error = getString(R.string.missing_prompt)
            return
        }

        setInputEnabled(false)
        response.text = ""
        timings.visibility = View.GONE
        rawHeader.visibility = View.GONE
        validatedHeader.visibility = View.GONE
        validatedJson.visibility = View.GONE
        status.setText(if (analysisMode) R.string.room_working else R.string.working)
        worker.execute {
            val result = runCatching {
                measureTimedValue {
                    val inference = if (analysisMode) {
                        runner.analyzeRoom(checkNotNull(image))
                    } else {
                        runner.infer(input, image)
                    }
                    val parsed = if (analysisMode) RoomAnalysisJson.parse(inference.rawResponse) else null
                    val json = (parsed as? RoomAnalysisParseResult.Success)?.let {
                        RoomAnalysisJson.encode(it.analysis)
                    }
                    RequestOutput(inference, parsed, json)
                }
            }
            if (result.exceptionOrNull() is OutOfMemoryError) {
                runCatching { runner.close() }
            }
            runOnUiThread {
                if (isFinishing || isDestroyed) return@runOnUiThread

                setInputEnabled(true)
                result.fold(
                    onSuccess = { showOutput(it.value, it.duration.inWholeMilliseconds) },
                    onFailure = { showError(it) },
                )
            }
        }
    }

    private fun showOutput(output: RequestOutput, totalMillis: Long) {
        val inference = output.inference
        response.text = inference.rawResponse
        timings.text = getString(
            R.string.request_timings,
            inference.initializationMillis?.let { getString(R.string.duration_ms, it) }
                ?: getString(R.string.engine_reused),
            inference.imageMillis?.let { getString(R.string.duration_ms, it) }
                ?: getString(R.string.no_image),
            inference.inferenceMillis,
            totalMillis,
            inference.backend.name,
        )
        timings.visibility = View.VISIBLE
        if (output.parsed != null) rawHeader.visibility = View.VISIBLE
        when (val parsed = output.parsed) {
            null -> status.setText(if (inference.rawResponse.isBlank()) R.string.empty_response else R.string.completed)
            is RoomAnalysisParseResult.Success -> {
                status.setText(R.string.room_json_success)
                validatedJson.text = output.validatedJson
                validatedHeader.visibility = View.VISIBLE
                validatedJson.visibility = View.VISIBLE
            }
            is RoomAnalysisParseResult.Failure -> {
                val reason = when (parsed.kind) {
                    RoomAnalysisParseResult.Failure.Kind.EMPTY -> R.string.empty_response
                    RoomAnalysisParseResult.Failure.Kind.INVALID_JSON -> R.string.room_invalid_json
                    RoomAnalysisParseResult.Failure.Kind.MISSING_FIELD -> R.string.room_missing_field
                    RoomAnalysisParseResult.Failure.Kind.WRONG_TYPE -> R.string.room_wrong_type
                    RoomAnalysisParseResult.Failure.Kind.UNSUPPORTED_VALUE -> R.string.room_unsupported_value
                    RoomAnalysisParseResult.Failure.Kind.UNKNOWN_FIELD -> R.string.room_unknown_field
                }
                status.text = getString(R.string.room_json_error, parsed.path, getString(reason))
            }
        }
    }

    private fun showError(error: Throwable) {
        status.text = when (error) {
            is OutOfMemoryError -> getString(R.string.memory_failed)
            is GemmaRunner.Failure -> when (error.code) {
                GemmaRunner.Error.STORAGE_UNAVAILABLE -> getString(R.string.storage_unavailable)
                GemmaRunner.Error.MODEL_MISSING -> getString(R.string.model_missing, error.detail)
                GemmaRunner.Error.MODEL_INIT_FAILED -> getString(R.string.model_failed, error.detail)
                GemmaRunner.Error.IMAGE_UNREADABLE -> getString(R.string.image_failed)
                GemmaRunner.Error.INFERENCE_FAILED -> getString(R.string.inference_failed, error.detail)
            }
            is Exception, is LinkageError -> getString(R.string.inference_failed, error.javaClass.simpleName)
            else -> throw error
        }
    }

    private data class RequestOutput(
        val inference: GemmaInference,
        val parsed: RoomAnalysisParseResult?,
        val validatedJson: String?,
    )

    private fun setInputEnabled(enabled: Boolean) {
        for (index in 0 until backends.childCount) backends.getChildAt(index).isEnabled = enabled
        prompt.isEnabled = enabled
        pickImage.isEnabled = enabled
        clearImage.isEnabled = enabled
        runInference.isEnabled = enabled
        analyzeRoom.isEnabled = enabled
    }

    @Suppress("DEPRECATION")
    private fun applySystemInsets(screen: View) {
        screen.setOnApplyWindowInsetsListener { view, insets ->
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                val safeArea = insets.getInsets(
                    WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout()
                        or WindowInsets.Type.ime(),
                )
                view.setPadding(safeArea.left, safeArea.top, safeArea.right, safeArea.bottom)
            }
            insets
        }
    }

    override fun onDestroy() {
        val session = runner
        // Queue closure behind any in-flight request on the same worker.
        worker.execute { session.close() }
        super.onDestroy()
    }

    private companion object {
        const val REQUEST_IMAGE = 1

        // Recreation cannot start a new engine until the previous activity's engine closes.
        val worker = Executors.newSingleThreadExecutor()
    }
}
