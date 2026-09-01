package com.voxa.app

import android.app.Application
import android.media.MediaPlayer
import android.media.MediaMetadataRetriever
import android.net.Uri
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.voxa.app.audio.AndroidAudioProcessor
import com.voxa.app.audio.ProcessingResult
import com.voxa.app.core.AudioFileFilter
import com.voxa.app.core.OutputNamer
import com.voxa.app.core.ParameterValidator
import com.voxa.app.data.AppDataRepository
import com.voxa.app.model.AudioFileItem
import com.voxa.app.model.BatchHistoryEntry
import com.voxa.app.model.ProcessingParameters
import com.voxa.app.model.ProcessingStatus
import com.voxa.app.model.Preset
import com.voxa.app.storage.StorageRepository
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.util.concurrent.CancellationException

data class MainUiState(
    val files: List<AudioFileItem> = emptyList(),
    val selectedIndex: Int? = null,
    val outputFolderUri: Uri? = null,
    val outputFolderLabel: String = "Choose output folder",
    val parameters: ProcessingParameters = ProcessingParameters(),
    val waveformPeaks: FloatArray = FloatArray(0),
    val isWaveformLoading: Boolean = false,
    val isPreviewPlaying: Boolean = false,
    val previewOutput: Boolean = false,
    val previewProgress: Float = 0f,
    val previewTimeText: String = "0:00",
    val isProcessing: Boolean = false,
    val overallProgress: Float = 0f,
    val statusText: String = "Add some audio files to get started.",
    val presets: List<Preset> = emptyList(),
    val selectedPresetName: String? = null,
    val newPresetName: String = "",
    val batchHistory: List<BatchHistoryEntry> = emptyList()
) {
    val selectedFile: AudioFileItem?
        get() = selectedIndex?.let { files.getOrNull(it) }

    val fileNamePreview: String
        get() = if (parameters.useCustomFileNames) {
            "Preview: my_recording.wav -> ${OutputNamer.previewExample(parameters.fileNamePattern, parameters.sequenceStart)}.${parameters.outputFormat}"
        } else {
            "Preview: my_recording.wav -> my_recording.${parameters.outputFormat}"
        }

    val canPreviewOutput: Boolean
        get() = selectedFile?.hasOutput == true

    val previewSourceLabel: String
        get() = if (previewOutput && canPreviewOutput) "Output preview" else "Input preview"

    val previewDisplayName: String
        get() = selectedFile?.let { file ->
            if (previewOutput && file.outputDisplayName != null) file.outputDisplayName else file.displayName
        }.orEmpty()
}

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val storage = StorageRepository(application)
    private val appData = AppDataRepository(application)
    private val processor = AndroidAudioProcessor(application, storage)
    private val mediaPlayer = MediaPlayer()
    private var playbackJob: Job? = null
    private var processingJob: Job? = null
    private var preparedPreviewUri: Uri? = null
    private var previewDurationMs: Int = 0

    private val _uiState = MutableStateFlow(MainUiState())
    val uiState: StateFlow<MainUiState> = _uiState.asStateFlow()

    init {
        mediaPlayer.setOnCompletionListener {
            playbackJob?.cancel()
            _uiState.update {
                it.copy(
                    isPreviewPlaying = false,
                    previewProgress = 1f,
                    previewTimeText = formatMillis(previewDurationMs)
                )
            }
            preparedPreviewUri = null
        }

        viewModelScope.launch {
            appData.presets.collect { presets ->
                _uiState.update { it.copy(presets = presets) }
            }
        }
        viewModelScope.launch {
            appData.history.collect { history ->
                _uiState.update { it.copy(batchHistory = history) }
            }
        }
    }

    fun addFiles(uris: List<Uri>) {
        viewModelScope.launch {
            val newItems = runCatching {
                uris.forEach(storage::persistReadPermission)
                withContext(Dispatchers.IO) { storage.buildFileItems(uris) }
            }.getOrElse { ex ->
                _uiState.update { it.copy(statusText = "Could not read the selected audio file: ${ex.message}") }
                return@launch
            }
            appendFiles(newItems)
        }
    }

    fun addFolder(uri: Uri) {
        storage.persistFolderPermission(uri)
        viewModelScope.launch {
            val newItems = runCatching {
                withContext(Dispatchers.IO) { storage.buildFileItemsFromFolder(uri) }
            }.getOrElse { ex ->
                _uiState.update { it.copy(statusText = "Could not read that folder: ${ex.message}") }
                return@launch
            }
            appendFiles(newItems)
        }
    }

    fun folderSelectionCancelled() {
        _uiState.update {
            it.copy(statusText = "No folder was selected. Choose a real subfolder, not the storage root.")
        }
    }

    private fun appendFiles(newItems: List<AudioFileItem>) {
        if (newItems.isEmpty()) {
            _uiState.update { it.copy(statusText = "Those files are not supported audio formats.") }
            return
        }

        _uiState.update { state ->
            val existing = state.files.map { it.uri }.toSet()
            val unique = newItems.filterNot { it.uri in existing }
            state.copy(
                files = state.files + unique,
                selectedIndex = state.selectedIndex ?: if (unique.isNotEmpty()) state.files.size else null,
                statusText = "Added ${unique.size} file(s). Ready when you are."
            )
        }

        loadWaveformForSelected()
    }

    fun chooseOutputFolder(uri: Uri) {
        storage.persistFolderPermission(uri)
        _uiState.update {
            it.copy(
                outputFolderUri = uri,
                outputFolderLabel = uri.lastPathSegment?.substringAfterLast(':') ?: "Output folder selected"
            )
        }
    }

    fun outputFolderSelectionCancelled() {
        _uiState.update {
            it.copy(statusText = "No output folder was selected. On Android, pick a subfolder like Download/ff.")
        }
    }

    fun selectFile(index: Int) {
        stopPreview()
        _uiState.update {
            it.copy(
                selectedIndex = index.takeIf { selected -> selected in it.files.indices },
                waveformPeaks = FloatArray(0),
                previewOutput = false,
                previewProgress = 0f,
                previewTimeText = "0:00"
            )
        }
        loadWaveformForSelected()
    }

    fun setPreviewOutput(enabled: Boolean) {
        val canUseOutput = enabled && _uiState.value.canPreviewOutput
        stopPreview()
        _uiState.update {
            it.copy(
                previewOutput = canUseOutput,
                waveformPeaks = FloatArray(0),
                previewProgress = 0f,
                previewTimeText = "0:00"
            )
        }
        loadWaveformForSelected()
    }

    fun removeSelected() {
        val selected = _uiState.value.selectedIndex ?: return
        stopPreview()
        _uiState.update { state ->
            val files = state.files.toMutableList().also { it.removeAt(selected) }
            state.copy(files = files, selectedIndex = files.indices.firstOrNull())
        }
        loadWaveformForSelected()
    }

    fun clearFiles() {
        stopPreview()
        _uiState.update {
            it.copy(
                files = emptyList(),
                selectedIndex = null,
                waveformPeaks = FloatArray(0),
                statusText = "Add some audio files to get started."
            )
        }
    }

    fun updateParameters(transform: (ProcessingParameters) -> ProcessingParameters) {
        _uiState.update { it.copy(parameters = transform(it.parameters)) }
    }

    fun updateNewPresetName(value: String) {
        _uiState.update { it.copy(newPresetName = value) }
    }

    fun applyPreset(name: String) {
        val preset = _uiState.value.presets.firstOrNull { it.name == name } ?: return
        _uiState.update {
            it.copy(
                parameters = preset.parameters,
                selectedPresetName = preset.name,
                statusText = "Loaded preset '${preset.name}'."
            )
        }
    }

    fun savePreset() {
        val state = _uiState.value
        val name = state.newPresetName.trim()
        if (name.isBlank()) {
            _uiState.update { it.copy(statusText = "Give your preset a name first.") }
            return
        }

        val errors = ParameterValidator.validate(state.parameters)
        if (errors.isNotEmpty()) {
            _uiState.update { it.copy(statusText = errors.first()) }
            return
        }

        viewModelScope.launch {
            val updated = state.presets
                .filterNot { it.name.equals(name, ignoreCase = true) } +
                Preset(name, state.parameters)
            appData.savePresets(updated)
            _uiState.update {
                it.copy(
                    presets = updated,
                    selectedPresetName = name,
                    newPresetName = "",
                    statusText = "Preset '$name' saved."
                )
            }
        }
    }

    fun deleteSelectedPreset() {
        val name = _uiState.value.selectedPresetName ?: return
        viewModelScope.launch {
            val updated = _uiState.value.presets.filterNot { it.name == name }
            appData.savePresets(updated)
            _uiState.update {
                it.copy(
                    presets = updated,
                    selectedPresetName = null,
                    statusText = "Preset '$name' deleted."
                )
            }
        }
    }

    fun togglePreview() {
        val file = _uiState.value.selectedFile ?: return
        val previewUri = previewUriFor(file)
        if (_uiState.value.isPreviewPlaying) {
            mediaPlayer.pause()
            playbackJob?.cancel()
            _uiState.update { it.copy(isPreviewPlaying = false) }
            return
        }

        if (preparedPreviewUri != previewUri) {
            try {
                mediaPlayer.reset()
                mediaPlayer.setDataSource(getApplication(), previewUri)
                mediaPlayer.prepare()
                preparedPreviewUri = previewUri
                previewDurationMs = mediaPlayer.duration.coerceAtLeast(0)
                mediaPlayer.seekTo((_uiState.value.previewProgress * previewDurationMs).toInt())
            } catch (ex: Exception) {
                preparedPreviewUri = null
                _uiState.update { it.copy(statusText = "Could not preview ${it.previewDisplayName}: ${ex.message}") }
                return
            }
        }

        mediaPlayer.start()
        _uiState.update { it.copy(isPreviewPlaying = true) }
        startPlaybackTicker()
    }

    fun seekPreview(progress: Float, commit: Boolean) {
        val clamped = progress.coerceIn(0f, 1f)
        val duration = previewDurationMillis()
        val targetMs = (duration * clamped).toInt()
        _uiState.update {
            it.copy(
                previewProgress = clamped,
                previewTimeText = formatMillis(targetMs)
            )
        }
        if (commit && duration > 0 && preparedPreviewUri != null) mediaPlayer.seekTo(targetMs)
    }

    fun startProcessing() {
        val state = _uiState.value
        if (state.files.isEmpty()) {
            _uiState.update { it.copy(statusText = "Add some audio files first.") }
            return
        }
        val outputFolder = state.outputFolderUri
        if (outputFolder == null) {
            _uiState.update { it.copy(statusText = "Choose an output folder first.") }
            return
        }

        val errors = ParameterValidator.validate(state.parameters)
        if (errors.isNotEmpty()) {
            _uiState.update { it.copy(statusText = errors.first()) }
            return
        }

        processingJob = viewModelScope.launch {
            _uiState.update {
                it.copy(
                    isProcessing = true,
                    overallProgress = 0f,
                    statusText = "Processing ${state.files.size} file(s)...",
                    files = it.files.map { file ->
                        file.copy(
                            status = ProcessingStatus.Pending,
                            statusMessage = "Waiting",
                            progress = 0f,
                            outputUri = null,
                            outputDisplayName = null
                        )
                    }
                )
            }

            val startedAt = System.currentTimeMillis()
            var succeeded = 0
            var failed = 0
            var skipped = 0

            try {
                state.files.forEachIndexed { index, file ->
                    _uiState.updateFile(index) {
                        it.copy(status = ProcessingStatus.Processing, statusMessage = "Processing...", progress = 0f)
                    }

                    val result = runCatching {
                        processor.processFile(
                            item = file,
                            outputFolderUri = outputFolder,
                            parameters = state.parameters,
                            sequenceNumber = state.parameters.sequenceStart + index,
                            onProgress = { progress ->
                                _uiState.updateFile(index) { it.copy(progress = progress) }
                            }
                        )
                    }.getOrElse { ex ->
                        ProcessingResult.Failed(ex.message ?: "Processing failed.")
                    }

                    when (result) {
                        is ProcessingResult.Success -> {
                            succeeded++
                            _uiState.updateFile(index) {
                                it.copy(
                                    status = ProcessingStatus.Success,
                                    statusMessage = "Saved as ${result.displayName}",
                                    progress = 1f,
                                    outputUri = result.outputUri,
                                    outputDisplayName = result.displayName
                                )
                            }
                            if (_uiState.value.selectedIndex == index && _uiState.value.previewOutput) {
                                loadWaveformForSelected()
                            }
                        }
                        is ProcessingResult.Failed -> {
                            failed++
                            _uiState.updateFile(index) {
                                it.copy(status = ProcessingStatus.Failed, statusMessage = result.message, progress = 0f)
                            }
                        }
                    }

                    val processed = index + 1
                    _uiState.update {
                        it.copy(
                            overallProgress = processed / state.files.size.toFloat(),
                            statusText = "Processed $processed of ${state.files.size} file(s)..."
                        )
                    }
                }
            } catch (ex: CancellationException) {
                skipped = state.files.count { it.status == ProcessingStatus.Pending }
                throw ex
            } finally {
                _uiState.update {
                    val finishedAt = System.currentTimeMillis()
                    val entry = BatchHistoryEntry(
                        startedAtEpochMillis = startedAt,
                        finishedAtEpochMillis = finishedAt,
                        totalFiles = state.files.size,
                        succeededCount = succeeded,
                        failedCount = failed,
                        skippedCount = skipped,
                        outputFormat = state.parameters.outputFormat,
                        wasCancelled = processingJob?.isCancelled == true
                    )
                    viewModelScope.launch {
                        appData.saveHistory(listOf(entry) + _uiState.value.batchHistory)
                    }

                    it.copy(
                        isProcessing = false,
                        statusText = "Finished: $succeeded succeeded, $failed failed, $skipped skipped.",
                        batchHistory = listOf(entry) + it.batchHistory
                    )
                }
            }
        }
    }

    fun cancelProcessing() {
        processingJob?.cancel()
        _uiState.update { it.copy(statusText = "Cancelling...") }
    }

    private fun loadWaveformForSelected() {
        val file = _uiState.value.selectedFile ?: return
        val previewItem = previewItemFor(file)
        viewModelScope.launch {
            _uiState.update { it.copy(isWaveformLoading = true) }
            val peaks = runCatching {
                processor.getWaveformPeaks(previewItem, 96)
            }.getOrElse { ex ->
                _uiState.update {
                    it.copy(
                        waveformPeaks = FloatArray(0),
                        isWaveformLoading = false,
                        statusText = "Could not draw ${it.previewSourceLabel.lowercase()}: ${ex.message}"
                    )
                }
                return@launch
            }
            previewDurationMs = withContext(Dispatchers.IO) { readDurationMillis(previewItem.uri) }
            _uiState.update {
                it.copy(
                    waveformPeaks = peaks,
                    isWaveformLoading = false,
                    previewTimeText = formatMillis(previewDurationMs)
                )
            }
        }
    }

    private fun startPlaybackTicker() {
        playbackJob?.cancel()
        playbackJob = viewModelScope.launch {
            while (mediaPlayer.isPlaying) {
                val duration = previewDurationMillis().coerceAtLeast(1)
                val current = mediaPlayer.currentPosition
                _uiState.update {
                    it.copy(
                        previewProgress = (current / duration.toFloat()).coerceIn(0f, 1f),
                        previewTimeText = formatMillis(current)
                    )
                }
                delay(33)
            }
            _uiState.update { it.copy(isPreviewPlaying = false) }
        }
    }

    private fun stopPreview() {
        playbackJob?.cancel()
        runCatching {
            mediaPlayer.stop()
            mediaPlayer.reset()
            preparedPreviewUri = null
            previewDurationMs = 0
        }
        _uiState.update { it.copy(isPreviewPlaying = false, previewProgress = 0f) }
    }

    private fun previewDurationMillis(): Int =
        runCatching {
            if (preparedPreviewUri == null) previewDurationMs else mediaPlayer.duration.coerceAtLeast(0)
        }.getOrDefault(previewDurationMs)

    private fun previewUriFor(file: AudioFileItem): Uri =
        if (_uiState.value.previewOutput && file.outputUri != null) file.outputUri else file.uri

    private fun previewItemFor(file: AudioFileItem): AudioFileItem =
        if (_uiState.value.previewOutput && file.outputUri != null) {
            file.copy(uri = file.outputUri, displayName = file.outputDisplayName ?: file.displayName)
        } else {
            file
        }

    private fun readDurationMillis(uri: Uri): Int {
        val retriever = MediaMetadataRetriever()
        return try {
            retriever.setDataSource(getApplication(), uri)
            retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION)?.toIntOrNull() ?: 0
        } catch (_: Exception) {
            0
        } finally {
            retriever.release()
        }
    }

    override fun onCleared() {
        playbackJob?.cancel()
        processingJob?.cancel()
        mediaPlayer.release()
        super.onCleared()
    }

    private fun MutableStateFlow<MainUiState>.updateFile(
        index: Int,
        transform: (AudioFileItem) -> AudioFileItem
    ) {
        update { state ->
            if (index !in state.files.indices) return@update state
            val files = state.files.toMutableList()
            files[index] = transform(files[index])
            state.copy(files = files)
        }
    }

    private fun formatMillis(ms: Int): String {
        val totalSeconds = ms / 1000
        val minutes = totalSeconds / 60
        val seconds = totalSeconds % 60
        return "$minutes:${seconds.toString().padStart(2, '0')}"
    }
}
