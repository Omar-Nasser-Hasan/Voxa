package com.voxa.app.core

object AudioFileFilter {
    val supportedOutputFormats = listOf("mp3", "wav", "m4a", "flac", "ogg", "aac")

    private val supportedInputExtensions = setOf(
        "mp3", "wav", "m4a", "aac", "flac", "ogg", "opus", "wma", "aiff", "aif", "amr"
    )

    fun isSupportedDisplayName(name: String): Boolean {
        val extension = name.substringAfterLast('.', missingDelimiterValue = "").lowercase()
        return extension in supportedInputExtensions
    }
}
