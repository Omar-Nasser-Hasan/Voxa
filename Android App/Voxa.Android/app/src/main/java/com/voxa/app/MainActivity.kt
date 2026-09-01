package com.voxa.app

import android.net.Uri
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.UploadFile
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.voxa.app.core.AudioFileFilter
import com.voxa.app.model.AudioFileItem
import com.voxa.app.model.ProcessingParameters
import com.voxa.app.model.ProcessingStatus
import kotlin.math.max

class MainActivity : ComponentActivity() {
    private val viewModel: MainViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            VoxaTheme {
                VoxaScreen(viewModel)
            }
        }
    }
}

@Composable
private fun VoxaTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = MaterialTheme.colorScheme.copy(
            primary = Color(0xFF5B4CE8),
            secondary = Color(0xFFA855DB)
        ),
        content = content
    )
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun VoxaScreen(viewModel: MainViewModel) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val filePicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
        viewModel.addFiles(uris)
    }
    val folderPicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri: Uri? ->
        if (uri != null) viewModel.addFolder(uri) else viewModel.folderSelectionCancelled()
    }
    val outputFolderPicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri: Uri? ->
        if (uri != null) viewModel.chooseOutputFolder(uri) else viewModel.outputFolderSelectionCancelled()
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text("Voxa", fontWeight = FontWeight.Bold)
                        Text("Batch audio converter", style = MaterialTheme.typography.bodySmall)
                    }
                }
            )
        },
        bottomBar = {
            BottomBar(
                state = state,
                onStart = viewModel::startProcessing,
                onCancel = viewModel::cancelProcessing
            )
        }
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(horizontal = 16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            item {
                ActionRow(
                    onAddFiles = { filePicker.launch(arrayOf("audio/*", "application/ogg")) },
                    onAddFolder = { folderPicker.launch(null) },
                    onChooseFolder = { outputFolderPicker.launch(null) },
                    onRemoveSelected = viewModel::removeSelected,
                    onClear = viewModel::clearFiles,
                    hasFiles = state.files.isNotEmpty(),
                    outputFolderLabel = state.outputFolderLabel
                )
            }

            item {
                AudioQueueCard(
                    files = state.files,
                    selectedIndex = state.selectedIndex,
                    onSelect = viewModel::selectFile
                )
            }

            item {
                WaveformCard(
                    state = state,
                    onTogglePreview = viewModel::togglePreview,
                    onPreviewOutputChange = viewModel::setPreviewOutput,
                    onSeek = viewModel::seekPreview
                )
            }

            item {
                SettingsCard(
                    parameters = state.parameters,
                    fileNamePreview = state.fileNamePreview,
                    onChange = viewModel::updateParameters
                )
            }

            item {
                PresetsCard(
                    state = state,
                    onPresetSelected = viewModel::applyPreset,
                    onPresetNameChange = viewModel::updateNewPresetName,
                    onSavePreset = viewModel::savePreset,
                    onDeletePreset = viewModel::deleteSelectedPreset
                )
            }

            item {
                HistoryCard(state.batchHistory)
            }

            item {
                Spacer(Modifier.height(88.dp))
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PresetsCard(
    state: MainUiState,
    onPresetSelected: (String) -> Unit,
    onPresetNameChange: (String) -> Unit,
    onSavePreset: () -> Unit,
    onDeletePreset: () -> Unit
) {
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainer)) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("Presets", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)

            var expanded by remember { mutableStateOf(false) }
            ExposedDropdownMenuBox(expanded = expanded, onExpandedChange = { expanded = !expanded }) {
                OutlinedTextField(
                    value = state.selectedPresetName ?: "No preset selected",
                    onValueChange = {},
                    readOnly = true,
                    label = { Text("Preset") },
                    trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
                    modifier = Modifier
                        .menuAnchor()
                        .fillMaxWidth()
                )
                ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                    state.presets.forEach { preset ->
                        DropdownMenuItem(
                            text = { Text(preset.name) },
                            onClick = {
                                expanded = false
                                onPresetSelected(preset.name)
                            }
                        )
                    }
                }
            }

            OutlinedTextField(
                value = state.newPresetName,
                onValueChange = onPresetNameChange,
                label = { Text("New preset name") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )

            Row(
                modifier = Modifier.horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Button(onClick = onSavePreset) { Text("Save") }
                Button(onClick = onDeletePreset, enabled = state.selectedPresetName != null) { Text("Delete") }
            }
        }
    }
}

@Composable
private fun HistoryCard(history: List<com.voxa.app.model.BatchHistoryEntry>) {
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainer)) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("Batch history", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
            if (history.isEmpty()) {
                Text("No batches finished yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                history.take(5).forEach { entry ->
                    Text(
                        "${entry.succeededCount} succeeded, ${entry.failedCount} failed, ${entry.skippedCount} skipped (${entry.outputFormat})",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
        }
    }
}

@Composable
private fun ActionRow(
    onAddFiles: () -> Unit,
    onAddFolder: () -> Unit,
    onChooseFolder: () -> Unit,
    onRemoveSelected: () -> Unit,
    onClear: () -> Unit,
    hasFiles: Boolean,
    outputFolderLabel: String
) {
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainer)) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Button(
                    onClick = onAddFiles,
                    modifier = Modifier.weight(1f),
                    contentPadding = ButtonDefaults.ButtonWithIconContentPadding
                ) {
                    Icon(Icons.Default.UploadFile, contentDescription = null)
                    Spacer(Modifier.width(6.dp))
                    Text("Add", maxLines = 1)
                }
                Button(
                    onClick = onAddFolder,
                    modifier = Modifier.weight(1f),
                    contentPadding = ButtonDefaults.ButtonWithIconContentPadding
                ) {
                    Icon(Icons.Default.Folder, contentDescription = null)
                    Spacer(Modifier.width(6.dp))
                    Text("Folder", maxLines = 1)
                }
                Button(
                    onClick = onChooseFolder,
                    modifier = Modifier.weight(1f),
                    contentPadding = ButtonDefaults.ButtonWithIconContentPadding
                ) {
                    Icon(Icons.Default.Folder, contentDescription = null)
                    Spacer(Modifier.width(6.dp))
                    Text("Output", maxLines = 1)
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                IconButton(onClick = onRemoveSelected, enabled = hasFiles) {
                    Icon(Icons.Default.Delete, contentDescription = "Remove selected")
                }
                Button(onClick = onClear, enabled = hasFiles) {
                    Text("Clear")
                }
            }
            Text(
                outputFolderLabel,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
        }
    }
}

@Composable
private fun AudioQueueCard(
    files: List<AudioFileItem>,
    selectedIndex: Int?,
    onSelect: (Int) -> Unit
) {
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainer)) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("Files", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
            if (files.isEmpty()) {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(120.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Text("Add audio files to begin", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            } else {
                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    files.forEachIndexed { index, file ->
                        FileRow(
                            file = file,
                            selected = selectedIndex == index,
                            onClick = { onSelect(index) }
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun FileRow(file: AudioFileItem, selected: Boolean, onClick: () -> Unit) {
    val statusColor = when (file.status) {
        ProcessingStatus.Success -> Color(0xFF2E7D32)
        ProcessingStatus.Failed -> Color(0xFFC62828)
        ProcessingStatus.Processing -> MaterialTheme.colorScheme.primary
        else -> MaterialTheme.colorScheme.onSurfaceVariant
    }

    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick),
        shape = RoundedCornerShape(8.dp),
        color = if (selected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface
    ) {
        Column(Modifier.padding(10.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    file.displayName,
                    modifier = Modifier.weight(1f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    fontWeight = FontWeight.Medium
                )
                Text(file.statusDisplay, color = statusColor, style = MaterialTheme.typography.bodySmall)
            }
            Text(
                file.statusMessage,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodySmall
            )
            if (file.status == ProcessingStatus.Processing) {
                LinearProgressIndicator(
                    progress = { file.progress },
                    modifier = Modifier.fillMaxWidth()
                )
            }
        }
    }
}

@Composable
private fun WaveformCard(
    state: MainUiState,
    onTogglePreview: () -> Unit,
    onPreviewOutputChange: (Boolean) -> Unit,
    onSeek: (Float, Boolean) -> Unit
) {
    val selected = state.selectedFile
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainer)) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("Preview", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                    if (selected != null) {
                        Text(
                            "${state.previewSourceLabel}: ${state.previewDisplayName}",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis
                        )
                    }
                }
                OutlinedButton(
                    onClick = { onPreviewOutputChange(false) },
                    enabled = state.previewOutput
                ) {
                    Text("Input")
                }
                Spacer(Modifier.width(6.dp))
                OutlinedButton(
                    onClick = { onPreviewOutputChange(true) },
                    enabled = state.canPreviewOutput && !state.previewOutput
                ) {
                    Text("Output")
                }
            }
            if (selected == null) {
                Text("Select a file to preview it.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                return@Column
            }

            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                IconButton(onClick = onTogglePreview) {
                    Icon(
                        if (state.isPreviewPlaying) Icons.Default.Pause else Icons.Default.PlayArrow,
                        contentDescription = if (state.isPreviewPlaying) "Pause" else "Play"
                    )
                }
                Text(
                    state.previewTimeText,
                    modifier = Modifier.width(48.dp),
                    fontWeight = FontWeight.SemiBold
                )
                WaveformCanvas(
                    peaks = state.waveformPeaks,
                    progress = state.previewProgress,
                    modifier = Modifier
                        .weight(1f)
                        .height(64.dp),
                    onSeek = onSeek
                )
            }
            if (state.isWaveformLoading) {
                Text("Loading waveform...", style = MaterialTheme.typography.bodySmall)
            }
        }
    }
}

@Composable
private fun WaveformCanvas(
    peaks: FloatArray,
    progress: Float,
    modifier: Modifier,
    onSeek: (Float, Boolean) -> Unit
) {
    val played = MaterialTheme.colorScheme.primary
    val unplayed = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.36f)

    Canvas(
        modifier = modifier
            .pointerInput(Unit) {
                detectTapGestures { offset ->
                    val nextProgress = (offset.x / max(size.width, 1)).coerceIn(0f, 1f)
                    onSeek(nextProgress, true)
                }
            }
            .pointerInput(Unit) {
            var dragProgress = 0f
            detectDragGestures(
                onDragStart = { offset ->
                    val nextProgress = (offset.x / max(size.width, 1)).coerceIn(0f, 1f)
                    dragProgress = nextProgress
                    onSeek(nextProgress, false)
                },
                onDrag = { change, _ ->
                    val nextProgress = (change.position.x / max(size.width, 1)).coerceIn(0f, 1f)
                    dragProgress = nextProgress
                    onSeek(nextProgress, false)
                },
                onDragEnd = { onSeek(dragProgress, true) }
            )
        }
    ) {
        val centerY = size.height / 2f
        val playheadX = size.width * progress.coerceIn(0f, 1f)
        val gap = 2.dp.toPx()
        val barWidth = ((size.width - gap * (peaks.size - 1)) / peaks.size.coerceAtLeast(1)).coerceAtLeast(2.dp.toPx())
        val maxHeight = size.height - 6.dp.toPx()

        peaks.forEachIndexed { index, peak ->
            val left = index * (barWidth + gap)
            val barHeight = (peak.coerceIn(0f, 1f) * maxHeight).coerceAtLeast(2.dp.toPx())
            drawRoundRect(
                color = if (left + barWidth / 2f <= playheadX) played else unplayed,
                topLeft = Offset(left, centerY - barHeight / 2f),
                size = Size(barWidth, barHeight),
                cornerRadius = androidx.compose.ui.geometry.CornerRadius(2.dp.toPx())
            )
        }

        drawLine(
            color = played,
            start = Offset(playheadX, 0f),
            end = Offset(playheadX, size.height),
            strokeWidth = 2.dp.toPx(),
            cap = StrokeCap.Round
        )
        drawCircle(Color.White, radius = 6.dp.toPx(), center = Offset(playheadX, centerY))
        drawCircle(played, radius = 6.dp.toPx(), center = Offset(playheadX, centerY), style = androidx.compose.ui.graphics.drawscope.Stroke(width = 2.dp.toPx()))
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SettingsCard(
    parameters: ProcessingParameters,
    fileNamePreview: String,
    onChange: ((ProcessingParameters) -> ProcessingParameters) -> Unit
) {
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainer)) {
        Column(
            modifier = Modifier
                .padding(12.dp)
                .fillMaxWidth(),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Text("Settings", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)

            FormatDropdown(parameters.outputFormat) {
                onChange { p -> p.copy(outputFormat = it) }
            }

            ToggleRow("Keep original sample rate", parameters.keepOriginalSampleRate) {
                onChange { p -> p.copy(keepOriginalSampleRate = it) }
            }
            OutlinedTextField(
                value = parameters.sampleRateHz.toString(),
                onValueChange = { value -> value.toIntOrNull()?.let { hz -> onChange { p -> p.copy(sampleRateHz = hz) } } },
                enabled = !parameters.keepOriginalSampleRate,
                label = { Text("Sample rate") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )

            SliderSetting("Volume", "${parameters.volumeChangeDb.toInt()} dB", parameters.volumeChangeDb.toFloat(), -30f..30f) {
                onChange { p -> p.copy(volumeChangeDb = it.toDouble()) }
            }
            ToggleRow("Normalize loudness", parameters.normalizeVolume) {
                onChange { p -> p.copy(normalizeVolume = it) }
            }
            ToggleRow("Enhance clarity", parameters.enhanceClarity) {
                onChange { p -> p.copy(enhanceClarity = it) }
            }
            SliderSetting("Speed", "${"%.2f".format(parameters.speedMultiplier)}x", parameters.speedMultiplier.toFloat(), 0.5f..2f) {
                onChange { p -> p.copy(speedMultiplier = it.toDouble()) }
            }
            SliderSetting("Start silence", "${"%.1f".format(parameters.silencePaddingStartSec)}s", parameters.silencePaddingStartSec.toFloat(), 0f..10f) {
                onChange { p -> p.copy(silencePaddingStartSec = it.toDouble()) }
            }
            SliderSetting("End silence", "${"%.1f".format(parameters.silencePaddingEndSec)}s", parameters.silencePaddingEndSec.toFloat(), 0f..10f) {
                onChange { p -> p.copy(silencePaddingEndSec = it.toDouble()) }
            }

            ToggleRow("Use custom output file names", parameters.useCustomFileNames) {
                onChange { p -> p.copy(useCustomFileNames = it) }
            }
            OutlinedTextField(
                value = parameters.fileNamePattern,
                onValueChange = { value -> onChange { p -> p.copy(fileNamePattern = value) } },
                enabled = parameters.useCustomFileNames,
                label = { Text("Filename pattern") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Text(fileNamePreview, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary)
        }
    }
}

@Composable
private fun ToggleRow(label: String, checked: Boolean, onCheckedChange: (Boolean) -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(label, modifier = Modifier.weight(1f))
        Checkbox(checked = checked, onCheckedChange = onCheckedChange)
    }
}

@Composable
private fun SliderSetting(label: String, valueText: String, value: Float, range: ClosedFloatingPointRange<Float>, onValueChange: (Float) -> Unit) {
    Column {
        Row {
            Text(label, modifier = Modifier.weight(1f))
            Text(valueText, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Slider(value = value, onValueChange = onValueChange, valueRange = range)
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun FormatDropdown(selected: String, onSelected: (String) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    ExposedDropdownMenuBox(expanded = expanded, onExpandedChange = { expanded = !expanded }) {
        OutlinedTextField(
            value = selected,
            onValueChange = {},
            readOnly = true,
            label = { Text("Output format") },
            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
            modifier = Modifier
                .menuAnchor()
                .fillMaxWidth()
        )
        ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            AudioFileFilter.supportedOutputFormats.forEach { format ->
                DropdownMenuItem(
                    text = { Text(format) },
                    onClick = {
                        expanded = false
                        onSelected(format)
                    }
                )
            }
        }
    }
}

@Composable
private fun BottomBar(state: MainUiState, onStart: () -> Unit, onCancel: () -> Unit) {
    Surface(shadowElevation = 8.dp) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            LinearProgressIndicator(progress = { state.overallProgress }, modifier = Modifier.fillMaxWidth())
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    state.statusText,
                    modifier = Modifier.weight(1f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    style = MaterialTheme.typography.bodySmall
                )
                if (state.isProcessing) {
                    Button(onClick = onCancel) { Text("Cancel") }
                } else {
                    Button(onClick = onStart) { Text("Start") }
                }
            }
        }
    }
}
