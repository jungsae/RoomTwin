package com.roomtwin.nativeai

import android.app.Activity
import android.os.Handler
import android.os.Looper
import android.widget.TextView

internal class StreamedReply(
    private val activity: Activity,
    private val text: TextView,
    private val onUpdate: () -> Unit,
) {
    private val handler = Handler(Looper.getMainLooper())
    private val pending = StringBuilder()
    private var scheduled = false
    private val flush = Runnable { flushText() }

    fun enqueue(chunk: String) {
        synchronized(pending) {
            pending.append(chunk)
            if (!scheduled) {
                scheduled = true
                handler.postDelayed(flush, 45)
            }
        }
    }

    fun finish(): Boolean {
        handler.removeCallbacks(flush)
        flushText()
        return text.text.isNotBlank()
    }

    private fun flushText() {
        val chunk = synchronized(pending) {
            scheduled = false
            pending.toString().also { pending.clear() }
        }
        if (!activity.isDestroyed && !activity.isFinishing && chunk.isNotEmpty()) {
            text.append(chunk)
            onUpdate()
        }
    }
}
