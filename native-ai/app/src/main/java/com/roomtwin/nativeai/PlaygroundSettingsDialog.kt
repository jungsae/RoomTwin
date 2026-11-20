package com.roomtwin.nativeai

import android.app.Activity
import android.app.AlertDialog
import android.text.InputFilter
import android.text.InputType
import android.view.View
import android.widget.ArrayAdapter
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.Switch
import android.widget.TextView
import kotlin.math.roundToInt

internal fun showPlaygroundSettings(
    activity: Activity,
    current: PlaygroundSettings,
    showBackend: Boolean = true,
    onApply: (PlaygroundSettings) -> Unit,
) {
    val density = activity.resources.displayMetrics.density
    val form = LinearLayout(activity).apply {
        orientation = LinearLayout.VERTICAL
        val inset = (20 * density).roundToInt()
        setPadding(inset, inset / 2, inset, inset / 2)
    }
    val scroll = ScrollView(activity).apply { addView(form) }

    fun addField(label: Int, input: View) {
        input.id = View.generateViewId()
        form.addView(TextView(activity).apply {
            setText(label)
            labelFor = input.id
            setPadding(0, (10 * density).roundToInt(), 0, 0)
        })
        form.addView(input)
    }

    fun input(label: Int, initial: String, type: Int, limit: Int): EditText =
        EditText(activity).apply {
            inputType = type
            importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
            filters = arrayOf(InputFilter.LengthFilter(limit))
            setText(initial)
            addField(label, this)
        }

    fun choice(label: Int, options: List<String>, selected: Int): Spinner =
        Spinner(activity).apply {
            adapter = ArrayAdapter(activity, android.R.layout.simple_spinner_dropdown_item, options)
            setSelection(selected)
            addField(label, this)
        }

    form.addView(TextView(activity).apply { setText(R.string.chat_settings_note) })
    val systemPrompt = input(
        R.string.chat_system_prompt,
        current.systemPrompt,
        InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE,
        PlaygroundSettings.MAX_SYSTEM_PROMPT_LENGTH,
    ).apply { maxLines = 4 }
    val decimal = InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_FLAG_DECIMAL
    val temperature = input(R.string.chat_temperature, current.temperature.toString(), decimal, 6)
    val topK = input(R.string.chat_top_k, current.topK.toString(), InputType.TYPE_CLASS_NUMBER, 3)
    val topP = input(R.string.chat_top_p, current.topP.toString(), decimal, 6)
    val tokens = input(R.string.chat_output_tokens, current.maxOutputTokens.toString(), InputType.TYPE_CLASS_NUMBER, 4)
    val contexts = PlaygroundSettings.CONTEXT_LENGTHS
    val context = choice(R.string.chat_context_length, contexts.map(Int::toString), contexts.indexOf(current.contextLength))
    val backends = InferenceBackend.entries
    val backend = if (showBackend) choice(R.string.chat_backend_setting, backends.map { it.name }, current.backend.ordinal) else null
    val thinking = Switch(activity).apply {
        setText(R.string.chat_thinking)
        isChecked = current.thinking
    }
    form.addView(thinking)
    val error = TextView(activity).apply {
        setText(R.string.chat_settings_invalid)
        visibility = View.GONE
        accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
    }
    form.addView(error)

    val dialog = AlertDialog.Builder(activity)
        .setTitle(R.string.chat_settings)
        .setView(scroll)
        .setNegativeButton(android.R.string.cancel, null)
        .setPositiveButton(R.string.chat_apply, null)
        .create()
    dialog.setOnShowListener {
        dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener {
            val next = try {
                PlaygroundSettings(
                    systemPrompt = systemPrompt.text.toString().trim(),
                    temperature = temperature.text.toString().replace(',', '.').toDouble(),
                    topK = topK.text.toString().toInt(),
                    topP = topP.text.toString().replace(',', '.').toDouble(),
                    maxOutputTokens = tokens.text.toString().toInt(),
                    contextLength = contexts[context.selectedItemPosition],
                    backend = backend?.let { backends[it.selectedItemPosition] } ?: current.backend,
                    thinking = thinking.isChecked,
                )
            } catch (_: IllegalArgumentException) {
                error.visibility = View.VISIBLE
                form.requestChildFocus(error, error)
                return@setOnClickListener
            }
            onApply(next)
            dialog.dismiss()
        }
    }
    dialog.show()
}
