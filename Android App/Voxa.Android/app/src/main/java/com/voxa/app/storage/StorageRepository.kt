package com.voxa.app.storage

import android.content.ContentResolver
import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import androidx.documentfile.provider.DocumentFile
import com.voxa.app.core.AudioFileFilter
import com.voxa.app.model.AudioFileItem
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

class StorageRepository(private val context: Context) {
    private val resolver: ContentResolver = context.contentResolver

    fun buildFileItems(uris: List<Uri>): List<AudioFileItem> =
        uris.mapNotNull { uri ->
            val metadata = readMetadata(uri)
            if (!AudioFileFilter.isSupportedDisplayName(metadata.displayName)) return@mapNotNull null

            AudioFileItem(
                uri = uri,
                displayName = metadata.displayName,
                mimeType = metadata.mimeType,
                sizeBytes = metadata.sizeBytes
            )
        }

    fun buildFileItemsFromFolder(folderUri: Uri): List<AudioFileItem> {
        val folder = DocumentFile.fromTreeUri(context, folderUri) ?: return emptyList()
        return folder.walkAudioFiles()
            .map { file ->
                AudioFileItem(
                    uri = file.uri,
                    displayName = file.name ?: "audio",
                    mimeType = file.type,
                    sizeBytes = file.length().takeIf { it >= 0 }
                )
            }
            .toList()
    }

    fun persistFolderPermission(uri: Uri) {
        runCatching {
            resolver.takePersistableUriPermission(
                uri,
                android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION or
                    android.content.Intent.FLAG_GRANT_WRITE_URI_PERMISSION
            )
        }
    }

    fun persistReadPermission(uri: Uri) {
        runCatching {
            resolver.takePersistableUriPermission(uri, android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
    }

    suspend fun copyInputToCache(item: AudioFileItem): File = withContext(Dispatchers.IO) {
        val safeName = item.displayName.replace(Regex("[^A-Za-z0-9._-]"), "_")
        val target = File(context.cacheDir, "input_${System.nanoTime()}_$safeName")

        resolver.openInputStream(item.uri).use { input ->
            requireNotNull(input) { "Could not open ${item.displayName}." }
            target.outputStream().use { output -> input.copyTo(output) }
        }

        target
    }

    suspend fun copyOutputToFolder(outputFile: File, outputFolderUri: Uri, displayName: String): Uri =
        withContext(Dispatchers.IO) {
            val folder = DocumentFile.fromTreeUri(context, outputFolderUri)
                ?: error("Could not open output folder.")
            val target = createUniqueFile(folder, displayName)
            resolver.openOutputStream(target.uri, "w").use { output ->
                requireNotNull(output) { "Could not create $displayName." }
                outputFile.inputStream().use { input -> input.copyTo(output) }
            }
            target.uri
        }

    private fun readMetadata(uri: Uri): FileMetadata {
        var displayName = uri.lastPathSegment?.substringAfterLast('/') ?: "audio"
        var sizeBytes: Long? = null

        resolver.query(uri, null, null, null, null)?.use { cursor ->
            val nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
            val sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE)
            if (cursor.moveToFirst()) {
                if (nameIndex >= 0) displayName = cursor.getString(nameIndex) ?: displayName
                if (sizeIndex >= 0 && !cursor.isNull(sizeIndex)) sizeBytes = cursor.getLong(sizeIndex)
            }
        }

        return FileMetadata(
            displayName = displayName,
            mimeType = resolver.getType(uri),
            sizeBytes = sizeBytes
        )
    }

    private fun createUniqueFile(folder: DocumentFile, displayName: String): DocumentFile {
        val base = displayName.substringBeforeLast('.', displayName)
        val extension = displayName.substringAfterLast('.', missingDelimiterValue = "")
        val mimeType = mimeTypeFor(extension)

        var candidateName = displayName
        var suffix = 1
        while (folder.findFile(candidateName) != null) {
            candidateName = if (extension.isBlank()) "${base}_$suffix" else "${base}_$suffix.$extension"
            suffix++
        }

        return folder.createFile(mimeType, candidateName)
            ?: error("Could not create $candidateName.")
    }

    private fun mimeTypeFor(extension: String): String =
        when (extension.lowercase()) {
            "mp3" -> "audio/mpeg"
            "wav" -> "audio/wav"
            "m4a" -> "audio/mp4"
            "aac" -> "audio/aac"
            "flac" -> "audio/flac"
            "ogg" -> "audio/ogg"
            else -> "application/octet-stream"
        }

    private fun DocumentFile.walkAudioFiles(): Sequence<DocumentFile> = sequence {
        listFiles().forEach { child ->
            when {
                child.isDirectory -> yieldAll(child.walkAudioFiles())
                child.isFile && AudioFileFilter.isSupportedDisplayName(child.name.orEmpty()) -> yield(child)
            }
        }
    }

    private data class FileMetadata(
        val displayName: String,
        val mimeType: String?,
        val sizeBytes: Long?
    )
}
