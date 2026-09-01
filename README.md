# Voxa

**Clean up, convert, and organize batches of audio files — without changing the originals.**

Voxa is a native Windows desktop app for creators, podcasters, and anyone who needs dependable batch audio processing without a complicated DAW workflow. Drop in files or folders, choose your output settings, and process the whole queue in one go.

English and Arabic are included in one application, with a right-to-left interface for Arabic.

> **Windows 10/11 · 64-bit · .NET 8 · Powered by FFmpeg**

## Contents

- [What Voxa does](#what-voxa-does)
- [Install and run](#install-and-run)
- [Using Voxa](#using-voxa)
- [First-run audio engine setup](#first-run-audio-engine-setup)
- [Output and safety](#output-and-safety)
- [Build from source](#build-from-source)
- [Create an installer](#create-an-installer)
- [Testing](#testing)
- [Project structure](#project-structure)
- [Release process](#release-process)
- [Troubleshooting](#troubleshooting)
- [FFmpeg and licensing](#ffmpeg-and-licensing)

## What Voxa does

| Area | Capability |
|---|---|
| Conversion | Export MP3, WAV, M4A, FLAC, OGG, or AAC. |
| Sample rate | Preserve the source sample rate or choose a value from 8,000 to 192,000 Hz. |
| Loudness | Apply manual gain or EBU R128 loudness normalization. |
| Speech cleanup | Apply light noise reduction, rumble removal, and a presence boost. |
| Speed | Change playback speed from 0.5× to 2.0× while preserving pitch. |
| Silence | Trim leading and trailing silence from a batch. |
| Metadata | Write title, artist, and album tags to supported output formats. |
| Naming | Use custom output filename patterns and collision-safe names. |
| Presets | Save personal presets or start with **Podcast — Spotify/Apple Ready** and **Voice Note Ready**. |
| Batch workflow | Add files or complete folders, follow progress per file, and cancel a batch safely. |
| Languages | Switch between English and Arabic; Arabic uses a mirrored RTL layout. |
| Appearance | Light and dark themes. |

## Install and run

1. Download `VoxaSetup.exe` from the [latest GitHub release](https://github.com/Omar-Nasser-Hasan/Voxa/releases/latest).
2. Run the installer and follow the short setup wizard.
3. Open **Voxa** from the Start menu or optional desktop shortcut.
4. Add audio files, select an output folder, choose settings, then select **Start Processing**.

The installer is per-user and does **not** require administrator rights. It installs under your Windows user profile and includes an uninstaller in **Installed apps**.

### Windows SmartScreen notice

Voxa is currently distributed without a code-signing certificate. Windows may show an **Unknown Publisher** or reputation warning when opening a newly downloaded installer. Download only from the official release page above. If you trust the download, select **More info → Run anyway**.

## Using Voxa

### 1. Add files

Use **Add Files**, **Add Folder**, or drag files and folders into the queue. Voxa ignores unsupported files and avoids adding the same item twice.

### 2. Choose your output

Set an output folder and choose the format you need. Use a filename pattern to keep large exports organized. Voxa automatically adds a suffix such as `_1` if a file name would collide with an existing output.

### 3. Adjust audio

Choose only the settings needed for the job:

- Keep the original sample rate or resample it.
- Raise/lower volume manually, normalize loudness, or both.
- Enable clarity for lightly cleaned-up speech.
- Trim silence at the beginning and end of recordings.
- Adjust playback speed without changing pitch.
- Add metadata when preparing a finished release.

### 4. Reuse settings

Select a built-in preset or save your own settings as a named preset. Custom presets are saved per Windows user and remain available after restarts and app updates.

### 5. Process safely

Select **Start Processing**. Progress is shown for each file. You can cancel at any time; Voxa stops the active work and leaves remaining queued files unprocessed. A failed or corrupted file is reported individually, while the rest of the batch continues.

### Arabic interface

Use the language control in the app to switch between English and Arabic. In Arabic mode, the interface uses right-to-left layout while file paths, filenames, numeric values, and units remain readable in their appropriate direction.

## First-run audio engine setup

Voxa uses FFmpeg for audio processing.

On first launch, Voxa looks for FFmpeg in this order:

1. An FFmpeg copy bundled next to the app.
2. Voxa’s per-user cache: `%LocalAppData%\Voxa\ffmpeg\`.
3. FFmpeg already available on the Windows `PATH`.
4. A one-time automatic download from an approved source.

The first launch needs internet access only when FFmpeg is not already available. Once Voxa caches it, normal processing works offline. If no internet connection is available and FFmpeg has not been installed or bundled, Voxa shows a clear Retry/Quit message instead of hanging.

## Output and safety

- **Original files are never changed or overwritten.** Processed audio is written to the selected output folder.
- Voxa checks for naming collisions and generates a safe unique name.
- A failed input does not stop a healthy batch.
- Disk space is checked before processing begins.
- Presets and batch history are stored in the current user’s application data, not beside source audio.

## Build from source

### Prerequisites

- Windows 10 or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optional: [Inno Setup 6](https://jrsoftware.org/isdl.php) to build the installer

### Run a development build

```powershell
git clone https://github.com/Omar-Nasser-Hasan/Voxa.git
cd Voxa/Windows App
dotnet restore
dotnet run
```

### Publish a self-contained build

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

This creates a `publish` folder that runs on supported 64-bit Windows machines without requiring a separate .NET runtime installation. You can also run `publish.bat` from the project root.

## Create an installer

1. Install Inno Setup 6.
2. From the project root, run `installer\build-installer.bat`.
3. Find the finished installer at `installer\Output\VoxaSetup.exe`.

The script publishes the app as self-contained and then compiles the Inno Setup installer. The result installs for the current Windows user, creates a Start menu entry, optionally creates a desktop shortcut, and supports clean uninstallation.

## Testing

Run the local regression suite from the project root:

```powershell
dotnet run --project Voxa.LocalTests\Voxa.LocalTests.csproj
```

The suite covers FFmpeg availability, supported formats, output naming, parameter validation, silence trimming, metadata arguments, preset availability, corrupted input handling, publishing, installer configuration, and version consistency.

Before each public release, also complete these manual checks:

- [ ] Install on a clean non-admin Windows account.
- [ ] Confirm the first-run FFmpeg download succeeds on an internet-connected machine.
- [ ] Confirm the offline Retry/Quit experience when no FFmpeg is available.
- [ ] Convert a mixed batch, then cancel while processing.
- [ ] Add a corrupted file beside valid audio and confirm the batch continues.
- [ ] Save a preset, restart Voxa, and verify it remains available.
- [ ] Uninstall Voxa and confirm app files are removed.
- [ ] Check Arabic mode with long labels, mixed-language filenames, and numeric controls.

## Project structure

```text
Windows App/
├── Assets/                 Application icon and logo
├── Commands/               WPF command implementations
├── Controls/               Shared styles and waveform control
├── Converters/             Binding converters
├── Localization/           English and Arabic resource dictionaries
├── Models/                 Queue, preset, processing, and history models
├── Services/               FFmpeg, output, presets, themes, localization, updates
├── Theme/                  Light and dark theme resources
├── ViewModels/             Main and setup window logic
├── Voxa.LocalTests/        Local regression test runner
├── installer/              Inno Setup script and build helper
├── App.xaml                WPF app startup
├── MainWindow.xaml         Main application window
├── SetupWindow.xaml        First-run setup window
├── Voxa.csproj             .NET/WPF project configuration
└── RELEASE_PLAN.md         Storefront and release checklist
```

## Release process

The shipping checklist and ready-to-use storefront copy live in [RELEASE_PLAN.md](RELEASE_PLAN.md).

Before building a release, update the same public version in all three places:

| File | Value |
|---|---|
| `Voxa.csproj` | `AssemblyVersion` and `FileVersion`, for example `1.0.1.0` |
| `Services/UpdateChecker.cs` | `AppVersion.Current`, for example `1.0.1` |
| `installer/Voxa.iss` | `MyAppVersion`, for example `1.0.1` |

Then run the test suite, build `VoxaSetup.exe`, and verify the installer from a buyer’s perspective before uploading it to the release page or storefront.

### Optional update notifications

Voxa can quietly check GitHub Releases and show an update banner. Before enabling it for a public build, configure `RepoOwner` and `RepoName` in `Services/UpdateChecker.cs` to the appropriate GitHub repository. The check is best-effort: a missing connection or unavailable API never blocks the app from opening.

## Troubleshooting

| Problem | What to do |
|---|---|
| First-run setup cannot download FFmpeg | Check internet access, choose **Retry**, or install/run with a bundled FFmpeg copy. |
| “FFmpeg was not found” | Restart Voxa to rerun setup. If the issue remains, reinstall and ensure the `ffmpeg` folder was not removed from the installation. |
| A file fails but others work | Inspect the item’s error detail. The source may be corrupted, protected, unsupported, or have an unusual codec. |
| Output is missing | Confirm the output folder is writable and has enough free storage. |
| A custom preset disappeared | Confirm you are signed into the same Windows account; presets are stored per user. |
| Windows warns about the installer | Download only from the official release page and follow the SmartScreen note above. |

## FFmpeg and licensing

Voxa uses FFmpeg as its audio-processing engine. When Voxa downloads FFmpeg on first run, it stores the downloaded executable and its included license text in the current user’s FFmpeg cache.

If you bundle FFmpeg yourself for an entirely offline distribution, include the license text that corresponds to the exact FFmpeg build you distribute. FFmpeg builds may be licensed under LGPL or GPL depending on their configuration. Review the build’s license terms before redistribution.

## Support

For bugs, feature requests, or installation help, open an issue in the [Voxa GitHub repository](https://github.com/Omar-Nasser-Hasan/Voxa/issues).
