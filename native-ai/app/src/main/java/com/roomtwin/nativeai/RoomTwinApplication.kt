package com.roomtwin.nativeai

import android.app.Application

class RoomTwinApplication : Application() {
    internal val inference = AppInference(this)
}
