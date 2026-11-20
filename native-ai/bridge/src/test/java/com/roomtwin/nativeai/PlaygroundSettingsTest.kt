package com.roomtwin.nativeai

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Test

class PlaygroundSettingsTest {
    @Test
    fun defaultsReuseCpuAndRoomAnalysisContext() {
        val settings = PlaygroundSettings()
        assertEquals(InferenceBackend.CPU, settings.backend)
        assertEquals(2048, settings.contextLength)
        assertFalse(settings.thinking)
    }

    @Test
    fun acceptsBoundariesAndMultilingualSystemPrompt() {
        val settings = PlaygroundSettings(
            systemPrompt = "영어와 한국어로 대답하세요. Answer in Korean and English.",
            temperature = 0.0,
            topK = 1,
            topP = 1.0,
            maxOutputTokens = 1024,
            contextLength = 8192,
            backend = InferenceBackend.GPU,
            thinking = true,
        )
        assertEquals(1024, settings.maxOutputTokens)
        assertEquals(InferenceBackend.GPU, settings.backend)
    }

    @Test
    fun rejectsUnsupportedValuesBeforeModelAllocation() {
        assertThrows(IllegalArgumentException::class.java) { PlaygroundSettings(temperature = Double.NaN) }
        assertThrows(IllegalArgumentException::class.java) { PlaygroundSettings(temperature = -0.1) }
        assertThrows(IllegalArgumentException::class.java) { PlaygroundSettings(topK = 0) }
        assertThrows(IllegalArgumentException::class.java) { PlaygroundSettings(topK = 65) }
        assertThrows(IllegalArgumentException::class.java) { PlaygroundSettings(topP = 1.01) }
        assertThrows(IllegalArgumentException::class.java) { PlaygroundSettings(maxOutputTokens = 1025) }
        assertThrows(IllegalArgumentException::class.java) { PlaygroundSettings(contextLength = 16384) }
        assertThrows(IllegalArgumentException::class.java) {
            PlaygroundSettings(systemPrompt = "a".repeat(PlaygroundSettings.MAX_SYSTEM_PROMPT_LENGTH + 1))
        }
    }
}
