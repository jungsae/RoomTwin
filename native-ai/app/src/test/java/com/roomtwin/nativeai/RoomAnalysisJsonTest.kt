package com.roomtwin.nativeai

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class RoomAnalysisJsonTest {
    @Test
    fun parsesAndSerializesRoom() {
        val result = RoomAnalysisJson.parse(ROOM) as RoomAnalysisParseResult.Success
        assertEquals("1.0", result.analysis.schemaVersion)
        assertEquals("bedroom", result.analysis.sceneType)
        assertEquals(listOf(VisualObservation("bed", listOf("white bedding"))), result.analysis.objects)
        assertEquals(listOf("window"), result.analysis.openings.map { it.type })
        assertEquals(listOf("An occluded object"), result.analysis.uncertainObservations)
        assertEquals(result, RoomAnalysisJson.parse(RoomAnalysisJson.encode(result.analysis)))
    }

    @Test
    fun acceptsEmptyArraysAndOneCompleteFence() {
        val empty = """{"schemaVersion":"1.0","sceneType":"unknown","objects":[],"openings":[],"uncertainObservations":[]}"""
        val wrapped = "  ```JSON\r\n$empty\r\n```  "
        val result = RoomAnalysisJson.parse(wrapped) as RoomAnalysisParseResult.Success
        assertTrue(result.analysis.objects.isEmpty())
        assertTrue(result.analysis.openings.isEmpty())
        assertTrue(result.analysis.uncertainObservations.isEmpty())
    }

    @Test
    fun rejectsBlankResponse() {
        expectFailure(" \n ", Kind.EMPTY, "$")
    }

    @Test
    fun rejectsMalformedJsonAndAdditionalOutput() {
        listOf(
            "{",
            ROOM.dropLast(1),
            ROOM.replace("\"bedroom\"", "'bedroom'"),
            ROOM.replace("\"bedroom\"", "/* comment */ \"bedroom\""),
            ROOM.replace("\"bedroom\"", "\"bedroom\"\n,\"invalid\":NaN"),
            "$ROOM $ROOM",
            "Here is the JSON: $ROOM",
            "```json\n$ROOM\n```\nDone.",
        ).forEach { expectFailure(it, Kind.INVALID_JSON, "$") }
    }

    @Test
    fun reportsMissingRootAndNestedFields() {
        expectFailure(ROOM.replace("\"schemaVersion\":\"1.0\",", ""), Kind.MISSING_FIELD, "$.schemaVersion")
        expectFailure(ROOM.replace(",\"attributes\":[\"white bedding\"]", ""), Kind.MISSING_FIELD, "$.objects[0].attributes")
    }

    @Test
    fun rejectsWrongTypesWithoutCoercion() {
        expectFailure("[]", Kind.WRONG_TYPE, "$")
        expectFailure(ROOM.replace("\"1.0\"", "1.0"), Kind.WRONG_TYPE, "$.schemaVersion")
        expectFailure(ROOM.replace("[\"white bedding\"]", "null"), Kind.WRONG_TYPE, "$.objects[0].attributes")
        expectFailure(ROOM.replace("\"white bedding\"", "true"), Kind.WRONG_TYPE, "$.objects[0].attributes[0]")
    }

    @Test
    fun rejectsUnknownTypesAndVersions() {
        expectFailure(ROOM.replace("\"bed\"", "\"car\""), Kind.UNSUPPORTED_VALUE, "$.objects[0].type")
        expectFailure(ROOM.replace("\"window\"", "\"cabinet\""), Kind.UNSUPPORTED_VALUE, "$.openings[0].type")
        expectFailure(ROOM.replace("\"1.0\"", "\"2.0\""), Kind.UNSUPPORTED_VALUE, "$.schemaVersion")
    }

    @Test
    fun rejectsUnexpectedPropertiesAndInvalidAttributes() {
        val extra = ROOM.replace("\"type\":\"bed\"", "\"type\":\"bed\",\"widthMeters\":2")
        expectFailure(extra, Kind.UNKNOWN_FIELD, "$.objects[0]")
        expectFailure(ROOM.replace("\"white bedding\"", "\"   \""), Kind.UNSUPPORTED_VALUE, "$.objects[0].attributes[0]")
    }

    private fun expectFailure(json: String, kind: Kind, path: String) {
        assertEquals(RoomAnalysisParseResult.Failure(kind, path), RoomAnalysisJson.parse(json))
    }

    private companion object {
        val ROOM = """
            {"schemaVersion":"1.0","sceneType":"bedroom",
             "objects":[{"type":"bed","attributes":["white bedding"]}],
             "openings":[{"type":"window","attributes":["partially visible"]}],
             "uncertainObservations":["An occluded object"]}
        """.trimIndent()
    }
}

private typealias Kind = RoomAnalysisParseResult.Failure.Kind
