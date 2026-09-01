package com.voxa.app.core

object OutputNamer {
    private val invalidChars = Regex("[\"<>|:*?\\\\/\\x00-\\x1F]")

    fun buildFileName(pattern: String, originalDisplayName: String, sequenceNumber: Int): String {
        val originalName = originalDisplayName.substringBeforeLast('.', originalDisplayName)
        val effectivePattern = pattern.ifBlank { "{name}" }

        val result = effectivePattern
            .replace("{name}", originalName, ignoreCase = true)
            .replace("{n4}", sequenceNumber.toString().padStart(4, '0'), ignoreCase = true)
            .replace("{n3}", sequenceNumber.toString().padStart(3, '0'), ignoreCase = true)
            .replace("{n2}", sequenceNumber.toString().padStart(2, '0'), ignoreCase = true)
            .replace("{n}", sequenceNumber.toString(), ignoreCase = true)
            .let { invalidChars.replace(it, "") }
            .trim()

        return result.ifBlank { originalName }
    }

    fun previewExample(pattern: String, sequenceStart: Int): String =
        buildFileName(pattern, "my_recording.wav", sequenceStart)
}
