package com.voxa.app.core

import com.voxa.app.model.ProcessingParameters

object ParameterValidator {
    private const val minSampleRate = 8000
    private const val maxSampleRate = 192000
    private const val minVolumeDb = -30.0
    private const val maxVolumeDb = 30.0
    private const val minSpeed = 0.25
    private const val maxSpeed = 4.0
    private const val minBitrate = 32
    private const val maxBitrate = 320
    private const val minSilencePaddingSec = 0.0
    private const val maxSilencePaddingSec = 30.0

    fun validate(parameters: ProcessingParameters): List<String> = buildList {
        if (parameters.outputFormat.lowercase() !in AudioFileFilter.supportedOutputFormats) {
            add("'${parameters.outputFormat}' is not a supported output format.")
        }

        if (!parameters.keepOriginalSampleRate &&
            parameters.sampleRateHz !in minSampleRate..maxSampleRate
        ) {
            add("Sample rate must be between $minSampleRate Hz and $maxSampleRate Hz.")
        }

        if (parameters.volumeChangeDb !in minVolumeDb..maxVolumeDb) {
            add("Volume change must be between $minVolumeDb dB and $maxVolumeDb dB.")
        }

        if (parameters.speedMultiplier !in minSpeed..maxSpeed) {
            add("Speed must be between ${minSpeed}x and ${maxSpeed}x.")
        }

        if (parameters.bitrateKbps !in minBitrate..maxBitrate) {
            add("Bitrate must be between $minBitrate kbps and $maxBitrate kbps.")
        }

        if (parameters.silencePaddingStartSec !in minSilencePaddingSec..maxSilencePaddingSec) {
            add("Starting silence must be between $minSilencePaddingSec and $maxSilencePaddingSec seconds.")
        }

        if (parameters.silencePaddingEndSec !in minSilencePaddingSec..maxSilencePaddingSec) {
            add("Ending silence must be between $minSilencePaddingSec and $maxSilencePaddingSec seconds.")
        }
    }
}
