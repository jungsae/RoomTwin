package com.roomtwin.nativeai.bridge

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class BridgeProtocolTest {
    private val valid = """{"contractVersion":"1.0","requestId":"photo-1","operation":"analyzeRoom","image":{"uri":"file:///data/photo.jpg","mimeType":"image/jpeg"}}"""

    @Test fun readsExpectedFields() {
        assertEquals(
            ImageRequest("photo-1", "file:///data/photo.jpg", "image/jpeg"),
            BridgeProtocol.readRequest(valid),
        )
        assertEquals("content://capture/photo.jpg", BridgeProtocol.readRequest(
            valid.replace("file:///data/photo.jpg", "content://capture/photo.jpg"),
        ).uri)
    }

    @Test fun refusesUnsupportedContractVersion() {
        val error = assertThrows(BridgeProtocol.RequestError::class.java) {
            BridgeProtocol.readRequest(valid.replace("1.0", "2.0"))
        }
        assertEquals("UNSUPPORTED_VERSION", error.code)
        assertEquals("photo-1", error.id)
    }

    @Test fun refusesInvalidJsonAndUnsupportedInput() {
        val invalid = listOf(
            "[]", "true", "{", "$valid {}", " ".repeat(8193),
            valid.replace("\"photo-1\"", "123"),
            valid.replace("photo-1", "invalid id"),
            valid.replace("image/jpeg", "image/webp"),
            valid.replace("file:///data/photo.jpg", "https://example.org/image.jpg"),
            valid.replace("analyzeRoom", "anotherOperation"),
        )
        for (json in invalid) {
            val error = assertThrows(BridgeProtocol.RequestError::class.java) { BridgeProtocol.readRequest(json) }
            assertEquals("INVALID_REQUEST", error.code)
        }
    }
}
