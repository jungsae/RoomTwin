package com.roomtwin.nativeai

data class NativeTurnMetrics(
    val initializationMillis: Long? = null,
    val runtimeTtftEstimateMillis: Double? = null,
    val prefillMillis: Double? = null,
    val decodeMillis: Double? = null,
    val inputTokens: Int? = null,
    val outputTokens: Int? = null,
    val prefillTokensPerSecond: Double? = null,
    val decodeTokensPerSecond: Double? = null,
    val contextTokens: Int? = null,
    val contextCapacity: Int? = null,
)

data class MeasuredTurn(val reply: VisionInference, val native: NativeTurnMetrics)
