# Voxa Android

Native Android version of Voxa, built with Kotlin and Jetpack Compose.

The desktop app downloads or locates `ffmpeg.exe`. Android cannot use Windows executables, so this app bundles FFmpeg through the maintained FFmpegKit Android package:

`dev.ffmpegkit-maintained:ffmpeg-kit-full:8.1.7`

Planned behavior:

- Pick audio files with Android's system file picker.
- Choose an output folder with Android's Storage Access Framework.
- Process files through FFmpegKit.
- Store presets and batch history locally.
- Preview selected audio with a waveform, play/pause, and seek.

Current status: MVP implementation in progress.

Implemented so far:

- Kotlin + Jetpack Compose Android project structure.
- File picker for individual audio files.
- Folder picker for recursive audio import.
- Output folder picker using Android's Storage Access Framework.
- Queue UI with per-file status/progress.
- Output format, sample rate, volume, normalize, clarity, speed, silence padding, and output filename controls.
- Waveform preview canvas with play/pause and tap/drag seeking.
- FFmpegKit processing wrapper.
- Presets and batch history storage using DataStore JSON.

To build, open this folder in Android Studio after installing the Android SDK, then run the `app` configuration.

This machine currently has Java, but no command-line Gradle or Android SDK visible on PATH. The next step is installing Android Studio, opening `Voxa.Android`, letting it sync Gradle dependencies, and fixing any compile issues Android Studio reports.
