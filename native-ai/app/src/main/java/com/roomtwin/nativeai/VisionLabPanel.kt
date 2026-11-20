package com.roomtwin.nativeai

import android.app.Activity
import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.text.InputFilter
import android.view.Gravity
import android.view.View
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import java.util.Locale
import kotlin.math.roundToInt

internal enum class LabPhotoAction { CAMERA, GALLERY, REMOVE, SAVE }

internal class VisionLabPanel(
    private val activity: Activity,
    root: View,
    onFeature: (LabFeature) -> Unit,
    onPhoto: (Int, LabPhotoAction) -> Unit,
) {
    private val content = root.findViewById<LinearLayout>(R.id.lab_content)
    private val scroll = root.findViewById<ScrollView>(R.id.lab_scroll)
    private val status = root.findViewById<TextView>(R.id.lab_status)
    val input: EditText = root.findViewById(R.id.lab_input)
    val runButton: Button = root.findViewById(R.id.lab_run)
    val cpuRetry: Button = root.findViewById(R.id.lab_cpu_retry)
    val settingsButton = button(R.string.chat_settings)
    val resetButton = button(R.string.lab_reset)

    private val menu = linkedMapOf<LabFeature, Button>()
    private val title = text(size = 23f, bold = true)
    private val note = text(size = 14f)
    private val backend = text(size = 13f)
    private val results = column()
    private val photoSlots = listOf(PhotoSlot(0, onPhoto), PhotoSlot(1, onPhoto))
    private val difficultyOptions = QuizDifficulty.entries
    private val difficulty = labeledOptions(
        R.string.lab_difficulty, listOf(R.string.lab_easy, R.string.lab_normal, R.string.lab_hard), 1,
    )
    private val language = labeledOptions(
        R.string.lab_target_language,
        listOf(R.string.lab_ko, R.string.lab_en, R.string.lab_ja, R.string.lab_zh, R.string.lab_es),
        0,
    )
    private val separate = CheckBox(activity).apply { setText(R.string.lab_summary_check) }

    var feature = LabFeature.DETECTIVE
        private set
    var hasConversation = false
        private set
    var settings = PlaygroundSettings(maxOutputTokens = 512)
        set(value) {
            field = value
            backend.text = activity.getString(R.string.chat_backend_label, value.backend.name)
        }
    val hasRecords: Boolean get() = results.childCount > 0

    init {
        val tools = row()
        tools.addView(backend, weighted())
        tools.addView(settingsButton)
        content.addView(tools)

        for (models in tiles.chunked(2)) {
            val menuRow = row()
            for (tile in models) {
                val item = button(tile.title).apply {
                    setOnClickListener { onFeature(tile.feature) }
                    minHeight = dp(50)
                }
                menu[tile.feature] = item
                menuRow.addView(item, weighted().apply { marginEnd = dp(4) })
            }
            content.addView(menuRow)
        }
        title.setPadding(0, dp(14), 0, dp(4))
        content.addView(title)
        note.setPadding(0, 0, 0, dp(10))
        content.addView(note)
        for (photo in photoSlots) content.addView(photo.container)
        content.addView(difficulty.first)
        content.addView(language.first)
        content.addView(separate)
        content.addView(resetButton)
        content.addView(results)
        input.filters = arrayOf(InputFilter.LengthFilter(PlaygroundSettings.MAX_QUESTION_LENGTH))
        settings = PlaygroundSettings(maxOutputTokens = 512)
        select(feature)
    }

    fun select(next: LabFeature) {
        feature = next
        val tile = tiles.first { it.feature == next }
        title.setText(tile.title)
        note.setText(tile.note)
        menu.forEach { (entry, button) ->
            button.isSelected = entry == next
            button.backgroundTintList = android.content.res.ColorStateList.valueOf(
                if (entry == next) Color.rgb(190, 222, 249) else Color.rgb(232, 239, 243),
            )
        }
        photoSlots[0].label.setText(if (next == LabFeature.CHANGES) R.string.lab_before else R.string.lab_photo)
        photoSlots[1].label.setText(R.string.lab_after)
        photoSlots[1].container.visibility = visibility(next == LabFeature.CHANGES)
        difficulty.first.visibility = visibility(next == LabFeature.QUIZ)
        language.first.visibility = visibility(next == LabFeature.TRANSLATE)
        separate.visibility = visibility(next == LabFeature.CHANGES)
        resetResults()
    }

    fun setPreview(index: Int, bitmap: Bitmap?) = photoSlots[index].show(bitmap)

    fun resetResults() {
        results.removeAllViews()
        hasConversation = false
        input.text.clear()
        refreshComposer()
        setStatus(null)
    }

    fun setStatus(message: CharSequence?, allowCpu: Boolean = false) {
        status.text = message
        status.visibility = visibility(!message.isNullOrBlank())
        cpuRetry.visibility = visibility(allowCpu)
    }

    fun setEnabled(enabled: Boolean) {
        (menu.values + listOf(input, runButton, cpuRetry, settingsButton, resetButton,
            difficulty.second, language.second, separate)).forEach { it.isEnabled = enabled }
        photoSlots.forEach { it.setEnabled(enabled) }
    }

    fun request(images: List<android.net.Uri>) = LabRequest(
        feature, settings, images, input.text.toString().trim(), hasConversation,
        difficultyOptions[difficulty.second.selectedItemPosition.coerceAtLeast(0)],
        language.second.selectedItem?.toString().orEmpty(),
        separate.isChecked,
    )

    fun startResult(request: LabRequest): Output {
        if (!request.followUp) results.removeAllViews()
        val card = column().apply {
            setPadding(dp(12), dp(12), dp(12), dp(12))
            background = background(Color.rgb(241, 246, 249))
        }
        val params = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT)
        params.topMargin = dp(10)
        results.addView(card, params)
        if (request.text.isNotBlank()) card.addView(text(bold = true).apply { this.text = request.text })
        val line = text()
        card.addView(line)
        scrollDown()
        return Output(card, line, StreamedReply(activity, line, ::scrollDown))
    }

    fun complete(output: Output, reply: LabReply) {
        output.stream.finish()
        val raw = reply.inference.text
        when (feature) {
            LabFeature.QUIZ -> {
                output.line.visibility = View.GONE
                when (val parsed = VisionLabJson.quiz(raw)) {
                    is LabQuizResult.Questions -> quiz(output.card, LabQuizSession(parsed.questions))
                    is LabQuizResult.NoEvidence -> output.card.addView(text().apply { text = parsed.reason })
                    null -> parseFailure(output.card, raw)
                }
            }
            LabFeature.FIND -> {
                output.line.visibility = View.GONE
                val finding = VisionLabJson.finding(raw)
                if (finding == null) parseFailure(output.card, raw) else showFinding(output.card, finding)
            }
            LabFeature.TRANSLATE -> {
                output.line.visibility = View.GONE
                val translation = VisionLabJson.translation(raw)
                if (translation == null) parseFailure(output.card, raw) else showTranslation(output.card, translation)
            }
            LabFeature.CHANGES -> {
                output.line.text = raw.ifBlank { activity.getString(R.string.empty_response) }
                val method = if (reply.comparison == LabComparison.DIRECT) R.string.lab_direct_method else R.string.lab_summary_method
                output.card.addView(text(size = 13f, bold = true).apply { setText(method) }, 0)
            }
            LabFeature.DETECTIVE -> {
                output.line.text = raw.ifBlank { activity.getString(R.string.empty_response) }
                if (raw.isNotBlank() && (!raw.contains("직접 보이는 사실") || !raw.contains("모델의 추측"))) {
                    output.card.addView(text(size = 13f).apply { setText(R.string.lab_missing_labels) })
                }
                // A blank terminal response can still possess a native conversation, but offers no useful history.
                hasConversation = raw.isNotBlank()
                refreshComposer()
            }
            LabFeature.SPACE -> output.line.text = raw.ifBlank { activity.getString(R.string.empty_response) }
        }
        addMetrics(output.card, reply.inference)
        input.text.clear()
        scrollDown()
    }

    fun failed(output: Output) {
        output.stream.finish()
        results.removeView(output.card)
        hasConversation = false
        refreshComposer()
    }

    private fun refreshComposer() {
        input.visibility = visibility(feature == LabFeature.FIND || hasConversation)
        input.setHint(if (hasConversation) R.string.lab_ask_hint else R.string.lab_item_hint)
        runButton.setText(if (hasConversation) R.string.lab_ask else R.string.lab_run)
    }

    private fun parseFailure(card: LinearLayout, raw: String) {
        card.addView(text(bold = true).apply { setText(R.string.lab_parse_error) })
        if (raw.isBlank()) return
        val original = text().apply { text = raw; visibility = View.GONE }
        card.addView(button(R.string.lab_show_raw).apply {
            setOnClickListener { original.visibility = visibility(original.visibility != View.VISIBLE) }
        })
        card.addView(original)
    }

    private fun quiz(card: LinearLayout, session: LabQuizSession) {
        val score = text(bold = true)
        fun updateScore() {
            score.text = activity.getString(R.string.lab_quiz_score, session.score, session.questions.size, session.answeredCount)
        }
        updateScore()
        card.addView(score)
        session.questions.forEachIndexed { index, question ->
            card.addView(text(bold = true).apply { text = "${index + 1}. ${question.question}" })
            val options = column()
            val explanation = text().apply { visibility = View.GONE }
            question.options.forEachIndexed { choiceIndex, label ->
                options.addView(Button(activity).apply {
                    text = label
                    isAllCaps = false
                    setOnClickListener {
                        val correct = session.answer(index, choiceIndex) ?: return@setOnClickListener
                        for (i in 0 until options.childCount) options.getChildAt(i).isEnabled = false
                        explanation.visibility = View.VISIBLE
                        explanation.text = buildString {
                            append(if (correct) activity.getString(R.string.lab_quiz_correct)
                                else activity.getString(R.string.lab_quiz_wrong, question.options[question.answerIndex]))
                            append("\n").append(activity.getString(R.string.lab_evidence))
                            append(" · ").append(question.evidence)
                            append("\n").append(question.explanation)
                        }
                        updateScore()
                    }
                })
            }
            card.addView(options)
            card.addView(explanation)
        }
    }

    private fun showFinding(card: LinearLayout, finding: LabFinding) {
        val presence = when (finding.presence) {
            LabPresence.FOUND -> R.string.lab_found
            LabPresence.NOT_OBSERVED -> R.string.lab_not_observed
            LabPresence.UNCERTAIN -> R.string.lab_uncertain
        }
        card.addView(text(bold = true).apply { setText(presence) })
        if (finding.presence == LabPresence.FOUND) {
            val region = when (finding.region) {
                LabRegion.LEFT -> R.string.lab_left
                LabRegion.CENTER -> R.string.lab_center
                LabRegion.RIGHT -> R.string.lab_right
                LabRegion.UNKNOWN -> R.string.lab_no_region
            }
            card.addView(text().apply { setText(region) })
        }
        if (finding.nearby.isNotBlank()) section(card, R.string.lab_nearby, finding.nearby)
        section(card, R.string.lab_evidence, finding.evidence)
    }

    private fun showTranslation(card: LinearLayout, translation: LabTranslation) {
        section(card, R.string.lab_source_text, translation.original.ifBlank { activity.getString(R.string.lab_no_text) })
        section(card, R.string.lab_translation_text, translation.translated.ifBlank { activity.getString(R.string.lab_no_text) })
        section(card, R.string.lab_unreadable_text, translation.unreadable.joinToString("\n").ifBlank {
            activity.getString(R.string.lab_none_reported)
        })
    }

    private fun section(card: LinearLayout, label: Int, value: String) {
        card.addView(text(size = 14f, bold = true).apply { setText(label) })
        card.addView(text().apply { text = value })
    }

    private fun addMetrics(card: LinearLayout, value: VisionInference) {
        val unavailable = activity.getString(R.string.lab_na)
        fun duration(millis: Long?) = millis?.let { activity.getString(R.string.duration_ms, it) } ?: unavailable
        val tokens = value.decodeTokens?.toString() ?: unavailable
        val speed = value.decodeTokensPerSecond?.let { String.format(Locale.getDefault(), "%.1f", it) } ?: unavailable
        card.addView(text(size = 12f).apply {
            text = activity.getString(R.string.lab_total_first, duration(value.totalMillis), duration(value.firstVisibleMillis), value.backend.name)
                .plus("\n").plus(activity.getString(R.string.lab_decode_metrics, tokens, speed))
            setPadding(0, dp(8), 0, 0)
        })
    }

    private fun labeledOptions(label: Int, values: List<Int>, selected: Int): Pair<LinearLayout, Spinner> {
        val spinner = Spinner(activity).apply {
            id = View.generateViewId()
            adapter = ArrayAdapter(activity, android.R.layout.simple_spinner_dropdown_item, values.map { activity.getString(it) })
            setSelection(selected)
        }
        val field = row().apply {
            gravity = Gravity.CENTER_VERTICAL
            addView(text(size = 14f).apply { setText(label); labelFor = spinner.id }, weighted())
            addView(spinner, weighted())
        }
        return field to spinner
    }

    private inner class PhotoSlot(index: Int, onPhoto: (Int, LabPhotoAction) -> Unit) {
        val container = column().apply { setPadding(0, dp(4), 0, dp(6)) }
        val label = text(size = 14f, bold = true)
        private val preview = ImageView(activity).apply {
            adjustViewBounds = true
            scaleType = ImageView.ScaleType.FIT_CENTER
            visibility = View.GONE
        }
        private val buttons = linkedMapOf(
            LabPhotoAction.CAMERA to button(R.string.chat_camera),
            LabPhotoAction.GALLERY to button(R.string.chat_gallery),
            LabPhotoAction.REMOVE to button(R.string.chat_detach),
            LabPhotoAction.SAVE to button(R.string.lab_save),
        )

        init {
            container.addView(label)
            container.addView(preview, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(150)))
            val select = row()
            val manage = row()
            buttons.forEach { (action, actionButton) ->
                actionButton.setOnClickListener { onPhoto(index, action) }
                val target = if (action == LabPhotoAction.CAMERA || action == LabPhotoAction.GALLERY) select else manage
                target.addView(actionButton, weighted())
            }
            container.addView(select)
            container.addView(manage)
            show(null)
        }

        fun show(bitmap: Bitmap?) {
            preview.setImageBitmap(bitmap)
            preview.contentDescription = label.text
            preview.visibility = visibility(bitmap != null)
            buttons.getValue(LabPhotoAction.REMOVE).visibility = preview.visibility
            buttons.getValue(LabPhotoAction.SAVE).visibility = preview.visibility
        }

        fun setEnabled(value: Boolean) = buttons.values.forEach { it.isEnabled = value }
    }

    private fun scrollDown() = scroll.post { scroll.smoothScrollTo(0, content.height) }
    private fun column() = LinearLayout(activity).apply { orientation = LinearLayout.VERTICAL }
    private fun row() = LinearLayout(activity).apply { orientation = LinearLayout.HORIZONTAL }
    private fun weighted() = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
    private fun dp(value: Int) = (value * activity.resources.displayMetrics.density).roundToInt()
    private fun visibility(value: Boolean) = if (value) View.VISIBLE else View.GONE
    private fun background(color: Int) = GradientDrawable().apply { setColor(color); cornerRadius = dp(14).toFloat() }
    private fun button(label: Int) = Button(activity).apply { setText(label); isAllCaps = false }
    private fun text(size: Float = 16f, bold: Boolean = false) = TextView(activity).apply {
        textSize = size
        setTextColor(Color.rgb(27, 46, 61))
        if (bold) setTypeface(null, Typeface.BOLD)
        setTextIsSelectable(size >= 16f)
    }

    data class Output(val card: LinearLayout, val line: TextView, val stream: StreamedReply)
    private data class Tile(val feature: LabFeature, val title: Int, val note: Int)

    private companion object {
        val tiles = listOf(
            Tile(LabFeature.DETECTIVE, R.string.lab_detective, R.string.lab_detective_note),
            Tile(LabFeature.QUIZ, R.string.lab_quiz, R.string.lab_quiz_note),
            Tile(LabFeature.CHANGES, R.string.lab_changes, R.string.lab_changes_note),
            Tile(LabFeature.FIND, R.string.lab_find, R.string.lab_find_note),
            Tile(LabFeature.TRANSLATE, R.string.lab_translate, R.string.lab_translate_note),
            Tile(LabFeature.SPACE, R.string.lab_space, R.string.lab_space_note),
        )
    }
}
