package com.voxa.app.model

import android.net.Uri
import kotlinx.serialization.Serializable

enum class ProcessingStatus {
    Pending,
    Processing,
    Success,
    Failed,
    Skipped
}

data class AudioFileItem(
    val uri: Uri,
    val displayName: String,
    val mimeType: String?,
    val sizeBytes: Long?,
    val status: ProcessingStatus = ProcessingStatus.Pending,
    val statusMessage: String = "Waiting",
    val progress: Float = 0f,
    val outputUri: Uri? = null,
    val outputDisplayName: String? = null
) {
    val statusDisplay: String
        get() = when (status) {
            ProcessingStatus.Pending -> "Waiting"
            ProcessingStatus.Processing -> "Processing"
            ProcessingStatus.Success -> "Done"
            ProcessingStatus.Failed -> "Failed"
            ProcessingStatus.Skipped -> "Skipped"
        }

    val hasOutput: Boolean
        get() = outputUri != null
}

@Serializable
data class ProcessingParameters(
    val outputFormat: String = "mp3",
    val sampleRateHz: Int = 44100,
    val keepOriginalSampleRate: Boolean = false,
    val volumeChangeDb: Double = 0.0,
    val enhanceClarity: Boolean = false,
    val normalizeVolume: Boolean = false,
    val speedMultiplier: Double = 1.0,
    val bitrateKbps: Int = 192,
    val silencePaddingStartSec: Double = 0.0,
    val silencePaddingEndSec: Double = 0.0,
    val fileNamePattern: String = "{name}",
    val useCustomFileNames: Boolean = true,
    val sequenceStart: Int = 1
)

@Serializable
data class Preset(
    val name: String,
    val parameters: ProcessingParameters
)

@Serializable
data class BatchHistoryEntry(
    val startedAtEpochMillis: Long,
    val finishedAtEpochMillis: Long,
    val totalFiles: Int,
    val succeededCount: Int,
    val failedCount: Int,
    val skippedCount: Int,
    val outputFormat: String,
    val wasCancelled: Boolean
)
