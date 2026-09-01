package com.voxa.app.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.voxa.app.model.BatchHistoryEntry
import com.voxa.app.model.Preset
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

private val Context.voxaDataStore by preferencesDataStore(name = "voxa_settings")

class AppDataRepository(private val context: Context) {
    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    val presets: Flow<List<Preset>> =
        context.voxaDataStore.data.map { preferences ->
            decodeList(preferences[PresetsKey])
        }

    val history: Flow<List<BatchHistoryEntry>> =
        context.voxaDataStore.data.map { preferences ->
            decodeList(preferences[HistoryKey])
        }

    suspend fun savePresets(presets: List<Preset>) {
        context.voxaDataStore.edit { preferences ->
            preferences[PresetsKey] = json.encodeToString(presets)
        }
    }

    suspend fun saveHistory(history: List<BatchHistoryEntry>) {
        context.voxaDataStore.edit { preferences ->
            preferences[HistoryKey] = json.encodeToString(history.take(50))
        }
    }

    private inline fun <reified T> decodeList(value: String?): List<T> =
        if (value.isNullOrBlank()) {
            emptyList()
        } else {
            runCatching { json.decodeFromString<List<T>>(value) }.getOrDefault(emptyList())
        }

    private companion object {
        val PresetsKey = stringPreferencesKey("presets_json")
        val HistoryKey = stringPreferencesKey("history_json")
    }
}
