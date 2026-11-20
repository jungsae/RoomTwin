package com.roomtwin.nativeai

import android.app.ActivityManager
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.net.Uri
import android.os.BatteryManager
import android.os.Build
import android.os.Debug
import android.os.PowerManager
import java.io.File
import java.io.IOException

internal class BenchSensors(private val app: Context) {
    val thermalStatus: Int?
        get() = if (Build.VERSION.SDK_INT >= 29) {
            runCatching { app.getSystemService(PowerManager::class.java)?.currentThermalStatus }.getOrNull()
        } else null

    fun isSevere(): Boolean = Build.VERSION.SDK_INT >= 29 &&
        (thermalStatus ?: -1) >= PowerManager.THERMAL_STATUS_SEVERE

    fun sample(): BenchResources {
        val runtime = Runtime.getRuntime()
        val battery = runCatching { app.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED)) }.getOrNull()
        val level = battery?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)?.takeIf { it >= 0 }
        val scale = battery?.getIntExtra(BatteryManager.EXTRA_SCALE, -1)?.takeIf { it > 0 }
        val available = runCatching {
            val manager = app.getSystemService(ActivityManager::class.java) ?: return@runCatching null
            ActivityManager.MemoryInfo().also(manager::getMemoryInfo).availMem.takeIf { it >= 0 }
        }.getOrNull()
        return BenchResources(
            runCatching { Debug.getPss().takeIf { it > 0 } }.getOrNull(),
            rssKiB(),
            runCatching { Debug.getNativeHeapAllocatedSize().takeIf { it >= 0 } }.getOrNull(),
            (runtime.totalMemory() - runtime.freeMemory()).takeIf { it >= 0 },
            available,
            thermalStatus,
            battery?.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, Int.MIN_VALUE)
                ?.takeIf { it != Int.MIN_VALUE }?.div(10.0),
            if (level != null && scale != null && level <= scale) 100 * level / scale else null,
        )
    }

    private fun rssKiB(): Long? = runCatching {
        if (Build.VERSION.SDK_INT >= 35) Debug.getRss().takeIf { it > 0 }
        else File("/proc/self/status").useLines { lines ->
            lines.firstOrNull { it.startsWith("VmRSS:") }
                ?.substringAfter(':')?.trim()?.substringBefore(' ')?.substringBefore('\t')?.toLongOrNull()
        }
    }.getOrNull()
}

internal class BenchPhotos(
    private val app: Context,
    private val configured: BenchConfig,
    incoming: List<Uri>,
) : AutoCloseable {
    private val files = mutableListOf<File>()
    val images: List<Uri> get() = files.map(Uri::fromFile)

    init {
        try {
            repeat(configured.photoCount) { index ->
                val local = File.createTempFile("roomtwin-benchmark-", ".img", app.cacheDir)
                files.add(local)
                if (configured.syntheticImages) renderFixture(local, index)
                else copy(incoming[index], local)
            }
        } catch (error: Throwable) {
            close()
            throw error
        }
    }

    private fun copy(uri: Uri, destination: File) {
        val input = app.contentResolver.openInputStream(uri) ?: throw IOException("IMAGE_OPEN_FAILED")
        input.use { source ->
            destination.outputStream().use { out ->
                val buffer = ByteArray(65536)
                var total = 0
                while (true) {
                    val count = source.read(buffer)
                    if (count < 0) break
                    total += count
                    if (total > MAX_FILE_BYTES) throw IOException("IMAGE_TOO_LARGE")
                    out.write(buffer, 0, count)
                }
                if (total == 0) throw IOException("IMAGE_EMPTY")
            }
        }
    }

    private fun renderFixture(destination: File, index: Int) {
        val image = Bitmap.createBitmap(576, 384, Bitmap.Config.ARGB_8888)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        val canvas = Canvas(image)
        canvas.drawColor(Color.rgb(232, 232, 232))
        try {
            val scenario = configured.visionCase.takeIf { configured.workload == BenchWorkload.VISION }
            when (scenario) {
                VisionCase.OCR -> {
                    paint.color = Color.BLACK
                    paint.textSize = 86f
                    paint.textAlign = Paint.Align.CENTER
                    canvas.drawText("LAB 2048", 288f, 218f, paint)
                }
                VisionCase.SURFACE -> {
                    paint.color = Color.rgb(170, 165, 160)
                    canvas.drawRect(0f, 315f, 576f, 384f, paint)
                    paint.color = Color.rgb(126, 75, 45)
                    canvas.drawRect(144f, 122f, 427f, 332f, paint)
                    paint.color = Color.rgb(78, 43, 28)
                    canvas.drawRect(281f, 122f, 288f, 332f, paint)
                    paint.color = Color.WHITE
                    canvas.drawCircle(263f, 219f, 7f, paint)
                    canvas.drawCircle(308f, 219f, 7f, paint)
                }
                else -> {
                    paint.color = Color.rgb(32, 84, 208)
                    canvas.drawRect(48f, 68f, 158f, 178f, paint)
                    canvas.drawRect(192f, 68f, 302f, 178f, paint)
                    if (index == 1) canvas.drawRect(48f, 227f, 158f, 337f, paint)
                    paint.color = Color.rgb(237, 177, 16)
                    canvas.drawCircle(453f, 122f, 52f, paint)
                }
            }
            destination.outputStream().use { check(image.compress(Bitmap.CompressFormat.JPEG, 94, it)) }
        } finally {
            image.recycle()
        }
    }

    override fun close() {
        files.forEach { it.delete() }
        files.clear()
    }

    private companion object { const val MAX_FILE_BYTES = 32 * 1024 * 1024 }
}
