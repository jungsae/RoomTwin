package com.roomtwin.nativeai

import android.app.Activity
import android.app.AlertDialog
import android.content.ActivityNotFoundException
import android.content.ClipData
import android.content.Intent
import android.graphics.ImageDecoder
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.MediaStore
import android.view.View
import android.view.WindowInsets
import android.widget.Button
import android.widget.RadioGroup
import android.widget.TextView
import androidx.core.content.FileProvider
import java.io.File
import java.util.concurrent.CancellationException
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.time.measureTimedValue

class MainActivity : Activity() {
    private val owner = Any()
    private val active = AtomicBoolean(true)
    private val inference get() = (application as RoomTwinApplication).inference
    private lateinit var modes: RadioGroup
    private lateinit var roomPanel: View
    private lateinit var playgroundRoot: View
    private lateinit var playground: PlaygroundPanel
    private lateinit var visionRoot: View
    private lateinit var visionLab: VisionLabPanel
    private lateinit var backends: RadioGroup
    private lateinit var pickImage: Button
    private lateinit var clearImage: Button
    private lateinit var analyzeRoom: Button
    private lateinit var status: TextView
    private lateinit var timings: TextView
    private lateinit var rawHeader: TextView
    private lateinit var validatedHeader: TextView
    private lateinit var validatedJson: TextView
    private lateinit var response: TextView
    private var selectedImage: Uri? = null
    private var playgroundImage: Attachment? = null
    private var pendingCapture: File? = null
    private var captureTarget = PLAYGROUND_IMAGE
    private val labImages = arrayOfNulls<Attachment>(2)
    private var pendingExport: Attachment? = null
    private var mode = R.id.room_mode
    private var updatingMode = false
    private var busy = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        applySystemInsets(findViewById(R.id.screen))

        modes = findViewById(R.id.modes)
        roomPanel = findViewById(R.id.room_panel)
        playgroundRoot = findViewById(R.id.playground_panel)
        playground = PlaygroundPanel(this, playgroundRoot)
        visionRoot = findViewById(R.id.vision_lab_panel)
        visionLab = VisionLabPanel(this, visionRoot, ::selectLabFeature, ::labPhotoAction)
        savedInstanceState?.getString(LAB_FEATURE)?.let { visionLab.select(LabFeature.valueOf(it)) }
        backends = findViewById(R.id.backends)
        pickImage = findViewById(R.id.pick_image)
        clearImage = findViewById(R.id.clear_image)
        analyzeRoom = findViewById(R.id.analyze_room)
        status = findViewById(R.id.status)
        timings = findViewById(R.id.timings)
        rawHeader = findViewById(R.id.raw_header)
        validatedHeader = findViewById(R.id.validated_header)
        validatedJson = findViewById(R.id.validated_json)
        response = findViewById(R.id.response)

        pendingCapture = savedInstanceState?.getString(PENDING_CAPTURE)?.let {
            File(captureDirectory, it)
        }
        captureTarget = savedInstanceState?.getInt(CAPTURE_TARGET, PLAYGROUND_IMAGE) ?: PLAYGROUND_IMAGE
        pendingExport = savedInstanceState?.getString(EXPORT_URI)?.let { stored ->
            Attachment(Uri.parse(stored), savedInstanceState.getString(EXPORT_FILE)?.let { File(captureDirectory, it) })
        }
        showMode(savedInstanceState?.getInt(MODE, R.id.room_mode) ?: R.id.room_mode)
        modes.setOnCheckedChangeListener { _, id ->
            if (!updatingMode && !busy && id != mode) requestMode(id)
        }
        backends.setOnCheckedChangeListener { _, _ ->
            clearRoomOutput()
            status.setText(R.string.ready)
        }
        pickImage.setOnClickListener { openImagePicker(ROOM_IMAGE) }
        clearImage.setOnClickListener { setSelectedImage(null) }
        analyzeRoom.setOnClickListener { submitRoom() }

        playground.galleryButton.setOnClickListener { openImagePicker(PLAYGROUND_IMAGE) }
        playground.cameraButton.setOnClickListener { openCamera(PLAYGROUND_IMAGE) }
        playground.detachButton.setOnClickListener { clearPlaygroundImage() }
        playground.sendButton.setOnClickListener { submitChat() }
        playground.resetButton.setOnClickListener {
            if (playground.hasTurns || playgroundImage != null) {
                AlertDialog.Builder(this)
                    .setMessage(R.string.chat_reset_confirmation)
                    .setNegativeButton(android.R.string.cancel, null)
                    .setPositiveButton(R.string.chat_reset) { _, _ -> resetChat() }
                    .show()
            } else resetChat()
        }
        playground.settingsButton.setOnClickListener {
            showPlaygroundSettings(this, playground.settings) { next ->
                if (next != playground.settings) {
                    playground.settings = next
                    resetChat()
                }
            }
        }
        visionLab.runButton.setOnClickListener { submitLab() }
        visionLab.resetButton.setOnClickListener {
            if (hasLabState()) {
                AlertDialog.Builder(this)
                    .setMessage(R.string.lab_reset_confirm)
                    .setNegativeButton(android.R.string.cancel, null)
                    .setPositiveButton(R.string.lab_reset) { _, _ -> resetLab() }
                    .show()
            }
        }
        visionLab.settingsButton.setOnClickListener {
            showPlaygroundSettings(this, visionLab.settings) { next ->
                if (next != visionLab.settings) {
                    visionLab.settings = next
                    inference.reset(owner)
                    visionLab.resetResults()
                    visionLab.setStatus(getString(R.string.lab_settings_changed))
                }
            }
        }
        visionLab.cpuRetry.setOnClickListener {
            val previousInput = visionLab.input.text.toString()
            visionLab.settings = visionLab.settings.copy(backend = InferenceBackend.CPU)
            inference.reset(owner)
            visionLab.resetResults()
            visionLab.input.setText(previousInput)
            submitLab()
        }
    }

    private fun requestMode(id: Int) {
        val confirmation = when {
            mode == R.id.playground_mode && (playground.hasTurns || playgroundImage != null) -> R.string.chat_leave_mode
            mode == R.id.vision_lab_mode && hasLabState() -> R.string.lab_leave_confirm
            else -> null
        }
        fun changeMode() {
            if (mode == R.id.playground_mode) resetChat()
            if (mode == R.id.vision_lab_mode) resetLab()
            showMode(id)
        }
        if (confirmation == null) {
            changeMode()
        } else {
            showMode(mode)
            AlertDialog.Builder(this)
                .setMessage(confirmation)
                .setNegativeButton(android.R.string.cancel, null)
                .setPositiveButton(R.string.chat_continue) { _, _ -> changeMode() }
                .show()
        }
    }

    private fun showMode(id: Int) {
        mode = id
        updatingMode = true
        modes.check(id)
        updatingMode = false
        roomPanel.visibility = if (id == R.id.room_mode) View.VISIBLE else View.GONE
        playgroundRoot.visibility = if (id == R.id.playground_mode) View.VISIBLE else View.GONE
        visionRoot.visibility = if (id == R.id.vision_lab_mode) View.VISIBLE else View.GONE
    }

    @Suppress("DEPRECATION")
    private fun openImagePicker(request: Int) {
        val picker = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = "image/*"
            putExtra(Intent.EXTRA_LOCAL_ONLY, true)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        try {
            startActivityForResult(picker, request)
        } catch (_: ActivityNotFoundException) {
            imageStatus(request, R.string.no_picker)
        }
    }

    @Suppress("DEPRECATION")
    private fun openCamera(target: Int) {
        val capture = runCatching {
            check(captureDirectory.isDirectory || captureDirectory.mkdirs())
            File.createTempFile("chat-", ".jpg", captureDirectory)
        }.getOrElse {
            imageStatus(target, R.string.image_failed)
            return
        }
        captureTarget = target
        pendingCapture = capture
        val uri = captureUri(capture)
        val intent = Intent(MediaStore.ACTION_IMAGE_CAPTURE).apply {
            putExtra(MediaStore.EXTRA_OUTPUT, uri)
            clipData = ClipData.newRawUri("RoomTwin image", uri)
            addFlags(CAMERA_GRANTS)
        }
        try {
            startActivityForResult(intent, PLAYGROUND_CAPTURE)
        } catch (_: ActivityNotFoundException) {
            discardCapture(capture, uri)
        } catch (_: SecurityException) {
            discardCapture(capture, uri)
        }
    }

    private fun discardCapture(file: File, uri: Uri) {
        pendingCapture = null
        revokeUriPermission(uri, CAMERA_GRANTS)
        inference.execute { file.delete() }
        imageStatus(captureTarget, R.string.chat_no_camera)
    }

    @Deprecated("Handles system picker and camera results.")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == LAB_EXPORT) {
            val original = pendingExport ?: return
            pendingExport = null
            val destination = data?.data
            if (resultCode == RESULT_OK && destination != null) savePhotoCopy(original, destination)
            else releaseExportIfDetached(original)
            return
        }
        if (requestCode == PLAYGROUND_CAPTURE) {
            val file = pendingCapture ?: return
            pendingCapture = null
            val uri = captureUri(file)
            revokeUriPermission(uri, CAMERA_GRANTS)
            if (resultCode == RESULT_OK) {
                loadPreview(Attachment(uri, file), captureTarget)
            } else {
                inference.execute { file.delete() }
                imageStatus(captureTarget, R.string.image_cancelled)
            }
            return
        }
        if (requestCode != ROOM_IMAGE && requestCode != PLAYGROUND_IMAGE && requestCode !in LAB_FIRST..LAB_LAST) return
        val image = data?.data
        when {
            resultCode != RESULT_OK -> imageStatus(requestCode, R.string.image_cancelled)
            image == null -> imageStatus(requestCode, R.string.image_failed)
            requestCode == ROOM_IMAGE -> {
                setSelectedImage(image)
                status.setText(R.string.ready)
            }
            else -> loadPreview(Attachment(image), requestCode)
        }
    }

    private fun imageStatus(request: Int, message: Int) {
        when (request) {
            ROOM_IMAGE -> status.setText(message)
            PLAYGROUND_IMAGE -> playground.setStatus(getString(message))
            else -> visionLab.setStatus(getString(message))
        }
    }

    private fun setSelectedImage(uri: Uri?) {
        selectedImage = uri
        pickImage.setText(if (uri == null) R.string.pick_image else R.string.change_image)
        clearImage.visibility = if (uri == null) View.GONE else View.VISIBLE
    }

    private fun loadPreview(attachment: Attachment, request: Int) {
        val labSlot = request - LAB_FIRST
        val activeTarget = when (request) {
            PLAYGROUND_IMAGE -> mode == R.id.playground_mode && !playground.imageInContext
            else -> mode == R.id.vision_lab_mode && labSlot in 0 until visionLab.feature.imageCount
        }
        if (!activeTarget) {
            releaseAttachment(attachment)
            return
        }
        setBusy(true)
        imageStatus(request, R.string.chat_image_loading)
        val resolver = contentResolver
        inference.execute {
            val preview = runCatching {
                ImageDecoder.decodeBitmap(ImageDecoder.createSource(resolver, attachment.uri)) { decoder, info, _ ->
                    decoder.allocator = ImageDecoder.ALLOCATOR_SOFTWARE
                    val width = info.size.width
                    val height = info.size.height
                    val longest = maxOf(width, height)
                    if (longest > 384) {
                        decoder.setTargetSize(
                            (width * 384L / longest).toInt().coerceAtLeast(1),
                            (height * 384L / longest).toInt().coerceAtLeast(1),
                        )
                    }
                }
            }
            runOnUiThread {
                if (isFinishing || isDestroyed) {
                    preview.getOrNull()?.recycle()
                    releaseAttachment(attachment)
                    return@runOnUiThread
                }
                setBusy(false)
                preview.fold(
                    onSuccess = {
                        if (request == PLAYGROUND_IMAGE) {
                            releaseAttachment(playgroundImage)
                            playgroundImage = attachment
                            playground.setPreview(it)
                            playground.setStatus(null)
                        } else {
                            releaseAttachment(labImages[labSlot])
                            labImages[labSlot] = attachment
                            visionLab.setPreview(labSlot, it)
                            inference.reset(owner)
                            visionLab.resetResults()
                        }
                    },
                    onFailure = {
                        releaseAttachment(attachment)
                        imageStatus(request, R.string.image_failed)
                    },
                )
            }
        }
    }

    private fun clearPlaygroundImage() {
        releaseAttachment(playgroundImage)
        playgroundImage = null
        playground.setPreview(null)
    }

    private fun releaseAttachment(attachment: Attachment?) {
        attachment?.file?.let { file -> inference.execute { file.delete() } }
    }

    private fun resetChat() {
        inference.reset(owner)
        clearPlaygroundImage()
        playground.clearTranscript()
        playground.question.text.clear()
    }

    private fun submitChat() {
        if (busy) return
        val text = playground.question.text.toString().trim()
        if (text.isBlank()) {
            playground.question.error = getString(R.string.missing_prompt)
            return
        }
        val attachment = playgroundImage
        val config = playground.settings
        val reply = playground.startReply(text, attachment != null)
        playground.question.text.clear()
        playground.setStatus(getString(R.string.working))
        setBusy(true)
        submit({ it.chat(config, text, attachment?.uri, reply::enqueue) }) { result ->
            val hasReply = reply.finish()
            result.fold(
                onSuccess = {
                    if (attachment != null) {
                        clearPlaygroundImage()
                        playground.acceptImageContext()
                    }
                    playground.setStatus(when {
                        !hasReply -> getString(R.string.empty_response)
                        playground.imageInContext -> getString(R.string.chat_image_context)
                        else -> null
                    })
                },
                onFailure = {
                    inference.reset(owner)
                    playground.clearTranscript()
                    playground.question.setText(text)
                    playground.setStatus(getString(R.string.chat_reset_after_error, requestError(it)))
                },
            )
        }
    }

    private fun hasLabState() = visionLab.hasRecords || labImages.any { it != null }

    private fun selectLabFeature(feature: LabFeature) {
        if (busy || feature == visionLab.feature) return
        fun openFeature() {
            resetLab()
            visionLab.select(feature)
        }
        if (hasLabState()) {
            AlertDialog.Builder(this)
                .setMessage(R.string.lab_reset_confirm)
                .setNegativeButton(android.R.string.cancel, null)
                .setPositiveButton(R.string.chat_continue) { _, _ -> openFeature() }
                .show()
        } else openFeature()
    }

    private fun labPhotoAction(slot: Int, action: LabPhotoAction) {
        if (busy || slot !in 0 until visionLab.feature.imageCount) return
        when (action) {
            LabPhotoAction.CAMERA -> openCamera(LAB_FIRST + slot)
            LabPhotoAction.GALLERY -> openImagePicker(LAB_FIRST + slot)
            LabPhotoAction.REMOVE -> {
                clearLabPhoto(slot)
                inference.reset(owner)
                visionLab.resetResults()
            }
            LabPhotoAction.SAVE -> labImages[slot]?.let { choosePhotoCopyDestination(it) }
        }
    }

    private fun clearLabPhoto(slot: Int) {
        releaseAttachment(labImages[slot])
        labImages[slot] = null
        visionLab.setPreview(slot, null)
    }

    private fun resetLab() {
        inference.reset(owner)
        labImages.indices.forEach(::clearLabPhoto)
        visionLab.resetResults()
    }

    @Suppress("DEPRECATION")
    private fun choosePhotoCopyDestination(photo: Attachment) {
        val mime = runCatching { contentResolver.getType(photo.uri) }.getOrNull()
            ?.takeIf { it.startsWith("image/") } ?: "image/jpeg"
        val suffix = android.webkit.MimeTypeMap.getSingleton().getExtensionFromMimeType(mime) ?: "jpg"
        val create = Intent(Intent.ACTION_CREATE_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = mime
            putExtra(Intent.EXTRA_LOCAL_ONLY, true)
            putExtra(Intent.EXTRA_TITLE, "${getString(R.string.lab_saved_copy)}.$suffix")
        }
        pendingExport = photo
        try {
            startActivityForResult(create, LAB_EXPORT)
        } catch (_: ActivityNotFoundException) {
            pendingExport = null
            visionLab.setStatus(getString(R.string.lab_save_failed))
        }
    }

    private fun savePhotoCopy(photo: Attachment, destination: Uri) {
        setBusy(true)
        val resolver = contentResolver
        inference.execute {
            val saved = runCatching {
                checkNotNull(resolver.openInputStream(photo.uri)).use { input ->
                    checkNotNull(resolver.openOutputStream(destination)).use { output -> input.copyTo(output) }
                }
            }.isSuccess
            runOnUiThread {
                releaseExportIfDetached(photo)
                if (isFinishing || isDestroyed) return@runOnUiThread
                setBusy(false)
                visionLab.setStatus(getString(if (saved) R.string.lab_saved else R.string.lab_save_failed))
            }
        }
    }

    private fun releaseExportIfDetached(photo: Attachment) {
        if (labImages.none { it?.uri == photo.uri }) releaseAttachment(photo)
    }

    private fun submitLab() {
        if (busy) return
        val followUp = visionLab.hasConversation
        val images = if (followUp) emptyList() else labImages.take(visionLab.feature.imageCount).mapNotNull { it?.uri }
        if (!followUp && images.size != visionLab.feature.imageCount) {
            visionLab.setStatus(getString(R.string.lab_pick_needed))
            return
        }
        val request = visionLab.request(images)
        if ((request.followUp || request.feature == LabFeature.FIND) && request.text.isBlank()) {
            visionLab.input.error = getString(R.string.missing_prompt)
            return
        }
        val output = visionLab.startResult(request)
        visionLab.setStatus(getString(R.string.working))
        setBusy(true)
        submit({ runner -> runner.runVisionLab(request, output.stream::enqueue) }) { result ->
            result.fold(
                onSuccess = {
                    visionLab.setStatus(null)
                    visionLab.complete(output, it)
                    if (it.inference.text.isBlank()) inference.reset(owner)
                },
                onFailure = { error ->
                    inference.reset(owner)
                    visionLab.failed(output)
                    val retry = request.settings.backend == InferenceBackend.GPU &&
                        (error is OutOfMemoryError || (error is GemmaRunner.Failure &&
                            error.code in listOf(GemmaRunner.Error.INFERENCE_FAILED, GemmaRunner.Error.MODEL_INIT_FAILED)))
                    visionLab.setStatus(requestError(error), allowCpu = retry)
                },
            )
        }
    }

    private fun clearRoomOutput() {
        response.text = ""
        timings.visibility = View.GONE
        rawHeader.visibility = View.GONE
        validatedHeader.visibility = View.GONE
        validatedJson.visibility = View.GONE
    }

    private fun submitRoom() {
        if (busy) return
        val image = selectedImage
        if (image == null) {
            status.setText(R.string.room_image_required)
            return
        }
        val backend = if (backends.checkedRadioButtonId == R.id.gpu) InferenceBackend.GPU else InferenceBackend.CPU
        setBusy(true)
        clearRoomOutput()
        status.setText(R.string.room_working)
        submit({ runner ->
            measureTimedValue {
                runner.selectBackend(backend)
                val inference = runner.analyzeRoom(image)
                val parsed = RoomAnalysisJson.parse(inference.rawResponse)
                val json = (parsed as? RoomAnalysisParseResult.Success)?.let { RoomAnalysisJson.encode(it.analysis) }
                RequestOutput(inference, parsed, json)
            }
        }) { result ->
            result.fold(
                onSuccess = { showOutput(it.value, it.duration.inWholeMilliseconds) },
                onFailure = { status.text = requestError(it) },
            )
        }
    }

    private fun <T> submit(action: (GemmaRunner) -> T, onResult: (Result<T>) -> Unit) {
        inference.submit(owner, action = { runner ->
            if (!active.get()) throw CancellationException()
            action(runner)
        }) { result ->
            runOnUiThread {
                if (isFinishing || isDestroyed) return@runOnUiThread
                setBusy(false)
                onResult(result)
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
        rawHeader.visibility = View.VISIBLE
        when (val parsed = output.parsed) {
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

    private fun requestError(error: Throwable): String = when (error) {
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

    private fun setBusy(value: Boolean) {
        busy = value
        for (group in listOf(modes, backends)) {
            for (index in 0 until group.childCount) group.getChildAt(index).isEnabled = !value
        }
        listOf(pickImage, clearImage, analyzeRoom).forEach { it.isEnabled = !value }
        playground.setEnabled(!value)
        visionLab.setEnabled(!value)
    }

    @Suppress("DEPRECATION")
    private fun applySystemInsets(screen: View) {
        screen.setOnApplyWindowInsetsListener { view, insets ->
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                val safeArea = insets.getInsets(
                    WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout() or WindowInsets.Type.ime(),
                )
                view.setPadding(safeArea.left, safeArea.top, safeArea.right, safeArea.bottom)
            }
            insets
        }
    }

    override fun onSaveInstanceState(outState: Bundle) {
        outState.putInt(MODE, mode)
        outState.putString(LAB_FEATURE, visionLab.feature.name)
        outState.putInt(CAPTURE_TARGET, captureTarget)
        pendingCapture?.let { outState.putString(PENDING_CAPTURE, it.name) }
        pendingExport?.let {
            outState.putString(EXPORT_URI, it.uri.toString())
            it.file?.let { file -> outState.putString(EXPORT_FILE, file.name) }
        }
        super.onSaveInstanceState(outState)
    }

    override fun onDestroy() {
        active.set(false)
        inference.release(owner)
        releaseAttachment(playgroundImage)
        labImages.forEach { photo ->
            if (photo?.uri != pendingExport?.uri || isFinishing) releaseAttachment(photo)
        }
        if (!isChangingConfigurations && isFinishing) {
            pendingExport?.let(::releaseExportIfDetached)
            pendingCapture?.let {
                revokeUriPermission(captureUri(it), CAMERA_GRANTS)
                inference.execute { it.delete() }
            }
        }
        super.onDestroy()
    }

    private val captureDirectory get() = File(cacheDir, "playground-capture")

    private fun captureUri(file: File): Uri =
        FileProvider.getUriForFile(this, "$packageName.playground-images", file)

    private data class Attachment(val uri: Uri, val file: File? = null)

    private data class RequestOutput(
        val inference: GemmaInference,
        val parsed: RoomAnalysisParseResult,
        val validatedJson: String?,
    )

    private companion object {
        const val MODE = "roomTwinMode"
        const val PENDING_CAPTURE = "pendingPlaygroundCapture"
        const val ROOM_IMAGE = 1
        const val PLAYGROUND_IMAGE = 2
        const val PLAYGROUND_CAPTURE = 3
        const val LAB_FIRST = 4
        const val LAB_LAST = 5
        const val LAB_EXPORT = 6
        const val LAB_FEATURE = "visionLabFeature"
        const val CAPTURE_TARGET = "visionLabCaptureTarget"
        const val EXPORT_URI = "visionLabExportUri"
        const val EXPORT_FILE = "visionLabExportFile"
        const val CAMERA_GRANTS = Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
    }
}
