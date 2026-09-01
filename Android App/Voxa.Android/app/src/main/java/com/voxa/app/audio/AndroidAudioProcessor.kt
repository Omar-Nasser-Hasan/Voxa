package com.voxa.app.audio

import android.content.Context
import android.net.Uri
import com.arthenica.ffmpegkit.FFmpegSession
import com.arthenica.ffmpegkit.FFmpegKit
import com.arthenica.ffmpegkit.FFprobeKit
import com.arthenica.ffmpegkit.ReturnCode
import com.voxa.app.core.OutputNamer
import com.voxa.app.model.AudioFileItem
import com.voxa.app.model.ProcessingParameters
import com.voxa.app.storage.StorageRepository
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileInputStream
import java.util.Locale
import kotlin.math.abs
import kotlin.math.roundToLong

class AndroidAudioProcessor(
    context: Context,
    private val storage: StorageRepository
) {
    private val cacheDir = File(context.cacheDir, "processed").also { it.mkdirs() }

    suspend fun processFile(
        item: AudioFileItem,
        outputFolderUri: Uri,
        parameters: ProcessingParameters,
        sequenceNumber: Int,
        onProgress: (Float) -> Unit
    ): ProcessingResult = withContext(Dispatchers.IO) {
        var inputFile: File? = null
        var tempOutput: File? = null

        try {
            inputFile = storage.copyInputToCache(item)
            val desiredBaseName = if (parameters.useCustomFileNames) {
                OutputNamer.buildFileName(parameters.fileNamePattern, item.displayName, sequenceNumber)
            } else {
                item.displayName.substringBeforeLast('.', item.displayName)
            }
            val outputDisplayName = "$desiredBaseName.${parameters.outputFormat.lowercase()}"
            tempOutput = File(cacheDir, "${System.nanoTime()}_$outputDisplayName")
            val durationSeconds = adjustedProgressDurationSeconds(probeDurationSeconds(inputFile), parameters)
            val command = buildCommand(inputFile, tempOutput, parameters)

            val session = executeWithProgress(command, durationSeconds, onProgress)
            if (!ReturnCode.isSuccess(session.returnCode)) {
                return@withContext ProcessingResult.Failed(
                    session.failStackTrace ?: session.allLogsAsString?.takeLast(800)
                    ?: "FFmpeg failed."
                )
            }

            if (!tempOutput.exists() || tempOutput.length() == 0L) {
                return@withContext ProcessingResult.Failed("FFmpeg finished but did not create an output file.")
            }

            onProgress(1f)
            val finalUri = storage.copyOutputToFolder(tempOutput, outputFolderUri, outputDisplayName)
            ProcessingResult.Success(finalUri, outputDisplayName)
        } catch (ex: CancellationException) {
            throw ex
        } catch (ex: Exception) {
            ProcessingResult.Failed(ex.message ?: "Processing failed.")
        } finally {
            inputFile?.delete()
            tempOutput?.delete()
        }
    }

    suspend fun getWaveformPeaks(item: AudioFileItem, bucketCount: Int): FloatArray = withContext(Dispatchers.IO) {
        if (bucketCount <= 0) return@withContext FloatArray(0)

        val inputFile = storage.copyInputToCache(item)
        val pcmFile = File(cacheDir, "${System.nanoTime()}_waveform.pcm")
        try {
            val command = listOf(
                "-y",
                "-hide_banner",
                "-loglevel", "error",
                "-i", commandArg(inputFile.absolutePath),
                "-ac", "1",
                "-ar", "8000",
                "-f", "s16le",
                commandArg(pcmFile.absolutePath)
            ).joinToString(" ")

            val session = FFmpegKit.execute(command)
            if (!ReturnCode.isSuccess(session.returnCode) || !pcmFile.exists()) return@withContext FloatArray(0)
            reducePcmToPeaks(pcmFile, bucketCount)
        } catch (_: Exception) {
            FloatArray(0)
        } finally {
            inputFile.delete()
            pcmFile.delete()
        }
    }

    private fun probeDurationSeconds(file: File): Double? {
        val session = FFprobeKit.getMediaInformation(file.absolutePath)
        val duration = session.mediaInformation?.duration ?: return null
        return duration.toDoubleOrNull()
    }

    private suspend fun executeWithProgress(
        command: String,
        durationSeconds: Double?,
        onProgress: (Float) -> Unit
    ): FFmpegSession {
        val completed = CompletableDeferred<FFmpegSession>()
        val session = FFmpegKit.executeAsync(
            command,
            { completed.complete(it) },
            { },
            { statistics ->
                if (durationSeconds != null && durationSeconds > 0) {
                    val progress = (statistics.time / (durationSeconds * 1000.0)).toFloat()
                    onProgress(progress.coerceIn(0f, 1f))
                }
            }
        )

        return try {
            completed.await()
        } catch (ex: CancellationException) {
            FFmpegKit.cancel(session.sessionId)
            throw ex
        }
    }

    private fun adjustedProgressDurationSeconds(sourceDurationSeconds: Double?, parameters: ProcessingParameters): Double? {
        if (sourceDurationSeconds == null || sourceDurationSeconds <= 0) return sourceDurationSeconds

        val speed = parameters.speedMultiplier.takeIf { it > 0.001 } ?: 1.0
        return sourceDurationSeconds / speed +
            parameters.silencePaddingStartSec.coerceAtLeast(0.0) +
            parameters.silencePaddingEndSec.coerceAtLeast(0.0)
    }

    private fun buildCommand(input: File, output: File, parameters: ProcessingParameters): String {
        val args = mutableListOf(
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-i", commandArg(input.absolutePath),
            "-vn"
        )

        val filters = buildFilterChain(parameters)
        if (filters.isNotEmpty()) {
            args += "-af"
            args += commandArg(filters.joinToString(","))
        }

        if (!parameters.keepOriginalSampleRate && parameters.sampleRateHz > 0) {
            args += "-ar"
            args += parameters.sampleRateHz.toString()
        }

        args += codecArgumentsFor(parameters.outputFormat, parameters.bitrateKbps)
        args += commandArg(output.absolutePath)
        return args.joinToString(" ")
    }

    private fun buildFilterChain(parameters: ProcessingParameters): List<String> = buildList {
        if (parameters.enhanceClarity) {
            add("highpass=f=80")
            add("afftdn=nf=-25")
            add("equalizer=f=3500:t=q:w=1:g=3")
        }

        if (abs(parameters.volumeChangeDb) > 0.001) {
            add("volume=${parameters.volumeChangeDb.formatFilterNumber()}dB")
        }

        if (parameters.normalizeVolume) {
            add("loudnorm=I=-16:LRA=11:TP=-1.5")
        }

        if (abs(parameters.speedMultiplier - 1.0) > 0.001) {
            addAll(buildAtempoChain(parameters.speedMultiplier))
        }

        if (parameters.silencePaddingStartSec > 0.001) {
            val ms = (parameters.silencePaddingStartSec * 1000).roundToLong()
            add("adelay=$ms:all=1")
        }

        if (parameters.silencePaddingEndSec > 0.001) {
            add("apad=pad_dur=${parameters.silencePaddingEndSec.formatFilterNumber()}")
        }
    }

    private fun buildAtempoChain(targetSpeed: Double): List<String> {
        var remaining = targetSpeed.coerceIn(0.25, 4.0)
        val stages = mutableListOf<String>()

        while (remaining > 2.0) {
            stages += "atempo=2.0"
            remaining /= 2.0
        }

        while (remaining < 0.5) {
            stages += "atempo=0.5"
            remaining /= 0.5
        }

        stages += "atempo=${remaining.formatFilterNumber()}"
        return stages
    }

    private fun codecArgumentsFor(format: String, bitrateKbps: Int): List<String> {
        val bitrate = bitrateKbps.coerceIn(32, 320)
        return when (format.lowercase()) {
            "mp3" -> listOf("-c:a", "libmp3lame", "-b:a", "${bitrate}k")
            "m4a", "aac" -> listOf("-c:a", "aac", "-b:a", "${bitrate}k")
            "wav" -> listOf("-c:a", "pcm_s16le")
            "flac" -> listOf("-c:a", "flac")
            "ogg" -> listOf("-c:a", "libvorbis", "-b:a", "${bitrate}k")
            else -> listOf("-b:a", "${bitrate}k")
        }
    }

    private fun reducePcmToPeaks(file: File, bucketCount: Int): FloatArray {
        val sampleCount = (file.length() / 2L).coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
        if (sampleCount == 0) return FloatArray(0)

        val peaks = FloatArray(bucketCount)
        val samplesPerBucket = (sampleCount / bucketCount).coerceAtLeast(1)

        FileInputStream(file).use { stream ->
            val buffer = ByteArray(32 * 1024)
            var sampleIndex = 0
            var carry: Int? = null

            while (true) {
                val read = stream.read(buffer)
                if (read <= 0) break

                var offset = 0
                if (carry != null && read > 0) {
                    val sample = ((carry!! and 0xff) or (buffer[0].toInt() shl 8)).toShort()
                    addPeak(peaks, sample, sampleIndex, samplesPerBucket)
                    sampleIndex++
                    carry = null
                    offset = 1
                }

                while (offset + 1 < read) {
                    val sample = ((buffer[offset].toInt() and 0xff) or (buffer[offset + 1].toInt() shl 8)).toShort()
                    addPeak(peaks, sample, sampleIndex, samplesPerBucket)
                    sampleIndex++
                    offset += 2
                }

                if (offset < read) carry = buffer[offset].toInt()
            }
        }

        return peaks
    }

    private fun addPeak(peaks: FloatArray, sample: Short, sampleIndex: Int, samplesPerBucket: Int) {
        val normalized = abs(sample.toInt()).coerceAtMost(Short.MAX_VALUE.toInt()) / Short.MAX_VALUE.toFloat()
        val bucket = (sampleIndex / samplesPerBucket).coerceAtMost(peaks.lastIndex)
        if (normalized > peaks[bucket]) peaks[bucket] = normalized
    }

    private fun commandArg(value: String): String =
        "\"${value.replace("\\", "\\\\").replace("\"", "\\\"")}\""

    private fun Double.formatFilterNumber(): String =
        String.format(Locale.US, "%.4f", this).trimEnd('0').trimEnd('.')
}

sealed interface ProcessingResult {
    data class Success(val outputUri: Uri, val displayName: String) : ProcessingResult
    data class Failed(val message: String) : ProcessingResult
}
