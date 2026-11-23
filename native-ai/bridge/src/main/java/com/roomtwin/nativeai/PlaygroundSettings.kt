package com.roomtwin.nativeai

data class PlaygroundSettings(
    val systemPrompt: String = "",
    val temperature: Double = 1.0,
    val topK: Int = 64,
    val topP: Double = 0.95,
    val maxOutputTokens: Int = 256,
    val contextLength: Int = DEFAULT_CONTEXT_LENGTH,
    val backend: InferenceBackend = InferenceBackend.CPU,
    val thinking: Boolean = false,
) {
    init {
        require(systemPrompt.length <= MAX_SYSTEM_PROMPT_LENGTH)
        require(temperature in 0.0..2.0)
        require(topK in 1..64)
        require(topP in 0.0..1.0)
        require(maxOutputTokens in 1..1024)
        require(contextLength in CONTEXT_LENGTHS)
    }

    companion object {
        const val DEFAULT_CONTEXT_LENGTH = 2048
        const val MAX_SYSTEM_PROMPT_LENGTH = 2000
        const val MAX_QUESTION_LENGTH = 4000
        val CONTEXT_LENGTHS = listOf(DEFAULT_CONTEXT_LENGTH, 4096, 8192)
    }
}
