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
import android.widget.TextView
import java.util.concurrent.Executors

class MainActivity : Activity() {
    private lateinit var runner: GemmaRunner
    private lateinit var prompt: EditText
    private lateinit var pickImage: Button
    private lateinit var clearImage: Button
    private lateinit var runInference: Button
    private lateinit var status: TextView
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
        status = findViewById(R.id.status)
        response = findViewById(R.id.response)

        pickImage.setOnClickListener { openImagePicker() }
        clearImage.setOnClickListener { setSelectedImage(null) }
        runInference.setOnClickListener { submit() }
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
        if (requestCode == REQUEST_IMAGE && resultCode == RESULT_OK) {
            data?.data?.let { setSelectedImage(it) }
        }
    }

    private fun setSelectedImage(uri: Uri?) {
        selectedImage = uri
        pickImage.setText(if (uri == null) R.string.pick_image else R.string.change_image)
        clearImage.visibility = if (uri == null) View.GONE else View.VISIBLE
    }

    private fun submit() {
        val input = prompt.text.toString().trim()
        if (input.isEmpty()) {
            prompt.error = getString(R.string.missing_prompt)
            return
        }

        val image = selectedImage
        setInputEnabled(false)
        response.text = ""
        status.setText(R.string.working)
        worker.execute {
            val result = runCatching { runner.infer(input, image) }
            runOnUiThread {
                if (isFinishing || isDestroyed) return@runOnUiThread

                setInputEnabled(true)
                result.fold(
                    onSuccess = { answer ->
                        if (answer.isEmpty()) {
                            status.setText(R.string.empty_response)
                        } else {
                            status.setText(R.string.completed)
                            response.text = answer
                        }
                    },
                    onFailure = { error ->
                        status.text = when (error) {
                            is GemmaRunner.Failure -> {
                                error.detail?.let { getString(error.messageId, it) }
                                    ?: getString(error.messageId)
                            }
                            is Exception, is LinkageError ->
                                getString(R.string.inference_failed, error.javaClass.simpleName)
                            else -> throw error
                        }
                    },
                )
            }
        }
    }

    private fun setInputEnabled(enabled: Boolean) {
        prompt.isEnabled = enabled
        pickImage.isEnabled = enabled
        clearImage.isEnabled = enabled
        runInference.isEnabled = enabled
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
