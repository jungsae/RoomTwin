package com.roomtwin.nativeai.bridge

import com.google.gson.GsonBuilder
import com.google.gson.JsonObject
import com.google.gson.JsonPrimitive
import com.google.gson.JsonSyntaxException
import com.google.gson.Strictness

internal data class ImageRequest(val id: String, val uri: String, val mimeType: String)

internal object BridgeProtocol {
    private const val VERSION = "1.0"
    private val gson = GsonBuilder().setStrictness(Strictness.STRICT).serializeNulls().create()
    private val idPattern = Regex("[A-Za-z0-9._-]{1,80}")

    fun validId(id: String): Boolean = idPattern.matches(id)

    fun readRequest(json: String): ImageRequest {
        val root = try {
            if (json.length <= 8192) gson.fromJson(json, JsonObject::class.java) else null
        } catch (_: JsonSyntaxException) {
            null
        } ?: throw RequestError(null, "INVALID_REQUEST")
        val id = root.string("requestId")?.takeIf(::validId)
        val version = root.string("contractVersion")
        if (version != null && version != VERSION) throw RequestError(id, "UNSUPPORTED_VERSION")
        val image = root["image"] as? JsonObject
        val uri = image?.string("uri")
        val mimeType = image?.string("mimeType")
        if (id == null || version == null || root.string("operation") != "analyzeRoom" ||
            root.keySet() != setOf("contractVersion", "requestId", "operation", "image") ||
            image == null || image.keySet() != setOf("uri", "mimeType") ||
            uri == null || !(uri.startsWith("content://") || uri.startsWith("file:///")) ||
            (mimeType != "image/jpeg" && mimeType != "image/png")
        ) {
            throw RequestError(id, "INVALID_REQUEST")
        }
        return ImageRequest(id, uri, mimeType)
    }

    fun reply(status: String, id: String? = null, fields: Map<String, Any?> = emptyMap()): String =
        gson.toJson(mapOf("contractVersion" to VERSION, "requestId" to id, "status" to status) + fields)

    fun error(code: String, id: String? = null, fieldPath: String? = null): String {
        val details = mutableMapOf("code" to code)
        if (fieldPath != null) details["fieldPath"] = fieldPath
        return reply("error", id, mapOf("error" to details))
    }

    private fun JsonObject.string(key: String): String? =
        (get(key) as? JsonPrimitive)?.takeIf { it.isString }?.asString

    class RequestError(val id: String?, val code: String) : Exception()
}
