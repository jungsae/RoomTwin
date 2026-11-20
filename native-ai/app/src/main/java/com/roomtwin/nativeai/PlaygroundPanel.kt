package com.roomtwin.nativeai

import android.app.Activity
import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.text.InputFilter
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import kotlin.math.roundToInt

internal class PlaygroundPanel(private val activity: Activity, private val root: View) {
    private val messages = root.findViewById<LinearLayout>(R.id.chat_messages)
    private val scroll = root.findViewById<ScrollView>(R.id.chat_scroll)
    private val backend = root.findViewById<TextView>(R.id.chat_backend)
    private val status = root.findViewById<TextView>(R.id.chat_status)
    private val preview = root.findViewById<ImageView>(R.id.chat_preview)

    val question: EditText = root.findViewById(R.id.chat_question)
    val settingsButton: Button = root.findViewById(R.id.chat_settings)
    val resetButton: Button = root.findViewById(R.id.chat_reset)
    val cameraButton: Button = root.findViewById(R.id.chat_camera)
    val galleryButton: Button = root.findViewById(R.id.chat_gallery)
    val detachButton: Button = root.findViewById(R.id.chat_detach)
    val sendButton: Button = root.findViewById(R.id.chat_send)

    private val emptyHint = TextView(activity).apply {
        setText(R.string.chat_empty)
        textSize = 16f
        setTextColor(Color.rgb(67, 79, 89))
        setPadding(0, dp(16), 0, dp(16))
    }

    var settings = PlaygroundSettings()
        set(value) {
            field = value
            backend.text = activity.getString(R.string.chat_backend_label, value.backend.name)
        }

    var hasTurns = false
        private set
    var imageInContext = false
        private set
    private var hasAttachment = false

    init {
        question.filters = arrayOf(InputFilter.LengthFilter(PlaygroundSettings.MAX_QUESTION_LENGTH))
        backend.text = activity.getString(R.string.chat_backend_label, settings.backend.name)
        messages.addView(emptyHint)
    }

    fun setEnabled(enabled: Boolean) {
        listOf(question, settingsButton, resetButton, cameraButton, galleryButton, detachButton, sendButton)
            .forEach { it.isEnabled = enabled }
    }

    fun setStatus(message: CharSequence?) {
        status.text = message
        status.visibility = if (message.isNullOrEmpty()) View.GONE else View.VISIBLE
    }

    fun setPreview(bitmap: Bitmap?) {
        hasAttachment = bitmap != null
        preview.setImageBitmap(bitmap)
        preview.visibility = if (hasAttachment) View.VISIBLE else View.GONE
        refreshAttachments()
    }

    fun acceptImageContext() {
        imageInContext = true
        refreshAttachments()
    }

    private fun refreshAttachments() {
        val canAttach = if (imageInContext) View.GONE else View.VISIBLE
        cameraButton.visibility = canAttach
        galleryButton.visibility = canAttach
        detachButton.visibility = if (hasAttachment) View.VISIBLE else View.GONE
    }

    fun clearTranscript() {
        messages.removeAllViews()
        messages.addView(emptyHint)
        hasTurns = false
        imageInContext = false
        refreshAttachments()
        setStatus(null)
    }

    fun startReply(text: String, includesImage: Boolean): StreamedReply {
        messages.removeView(emptyHint)
        hasTurns = true
        val userText = if (includesImage) activity.getString(R.string.chat_attached) + "\n" + text else text
        addBubble(R.string.chat_you, own = true).text = userText
        return StreamedReply(activity, addBubble(R.string.chat_model, own = false), ::scrollToLatest)
    }

    private fun addBubble(label: Int, own: Boolean): TextView {
        val card = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(14), dp(10), dp(14), dp(12))
            background = GradientDrawable().apply {
                cornerRadius = dp(14).toFloat()
                setColor(if (own) Color.rgb(221, 238, 254) else Color.rgb(242, 245, 247))
            }
        }
        val params = LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            LinearLayout.LayoutParams.WRAP_CONTENT,
        ).apply {
            setMargins(if (own) dp(40) else 0, 0, if (own) 0 else dp(32), dp(10))
        }
        messages.addView(card, params)
        card.addView(TextView(activity).apply {
            setText(label)
            setTypeface(null, Typeface.BOLD)
            setTextColor(Color.rgb(40, 63, 84))
            textSize = 12f
        })
        val text = TextView(activity).apply {
            textSize = 16f
            setTextColor(Color.rgb(25, 35, 44))
            setTextIsSelectable(true)
        }
        card.addView(text)
        scrollToLatest()
        return text
    }

    private fun scrollToLatest() {
        scroll.post { scroll.smoothScrollTo(0, messages.height) }
    }

    private fun dp(value: Int) = (value * root.resources.displayMetrics.density).roundToInt()

}
