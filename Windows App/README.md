# Voxa

A single-page WPF desktop app for batch-processing audio files: format conversion,
sample rate changes, volume adjustment, clarity enhancement/normalization, and
playback speed — all driven by FFmpeg, with save/load/delete presets and a
responsive, non-blocking UI.

Original files are **never** modified. Every processed file is written to a
separate output folder you choose.

**Zero manual setup for the end user.** The app does not require FFmpeg to be
pre-installed or bundled. The first time it's launched on a machine, a short
"Setting things up..." screen appears, FFmpeg is downloaded automatically and
cached in the user's own profile, and the main app opens right after — no
command line, no separate installer, nothing for a non-technical person to go
find or configure. Every launch after that opens instantly, and works fully
offline from then on.

---

## 1. Project layout

```
Voxa/
├── Voxa.csproj
├── App.xaml / App.xaml.cs
├── SetupWindow.xaml / SetupWindow.xaml.cs   (first-run "setting things up" screen)
├── MainWindow.xaml / MainWindow.xaml.cs
├── Assets/
│   ├── voxa.ico                  (embedded app icon)
│   └── voxa-mark.png             (in-app logo mark)
├── Theme/
│   ├── LightTheme.xaml
│   └── DarkTheme.xaml
├── Controls/
│   └── Styles.xaml               (shared card/button/slider/switch styles)
├── Models/
│   ├── AudioFileItem.cs          (one row in the batch queue)
│   ├── ProcessingParameters.cs   (format, sample rate, volume, speed, etc.)
│   ├── Preset.cs
│   ├── BatchHistoryEntry.cs
│   └── ProcessingStatus.cs
├── Services/
│   ├── FFmpegService.cs          (builds & runs the FFmpeg command, parses progress)
│   ├── FFmpegBootstrapper.cs     (finds or downloads+caches FFmpeg, no user action needed)
│   ├── PresetManager.cs          (JSON persistence in %AppData%)
│   ├── BatchHistoryManager.cs    (JSON log of past batches)
│   ├── DiskSpaceChecker.cs       (warns before a batch if the output drive is low)
│   ├── OutputNamer.cs            (collision-safe output filenames)
│   ├── ThemeService.cs           (light/dark theme swap + persistence)
│   ├── UpdateChecker.cs          (optional background GitHub-release check)
│   ├── AudioFileFilter.cs        (supported extensions)
│   └── ParameterValidator.cs
├── ViewModels/
│   ├── MainViewModel.cs          (all app logic / batch loop)
│   ├── SetupViewModel.cs         (drives the first-run setup screen)
│   └── ViewModelBase.cs
├── Commands/
│   └── RelayCommand.cs           (RelayCommand + AsyncRelayCommand)
├── Converters/
│   ├── StatusToColorConverter.cs
│   ├── InverseBooleanConverter.cs
│   ├── PeakToHeightConverter.cs  (waveform bar heights)
│   └── PercentToScaleConverter.cs
├── installer/
│   ├── Voxa.iss                  (Inno Setup script → VoxaSetup.exe)
│   └── build-installer.bat       (publish + compile installer, one click)
├── publish.bat                   (Option A/B self-contained build, no installer)
└── ffmpeg/
    └── (optional — see step 2)
```

## 2. FFmpeg: automatic by default, manual bundling optional

**You don't need to do anything here for most cases.** On first launch, `App.xaml.cs`
opens `SetupWindow` before `MainWindow`. `SetupWindow` runs `FFmpegBootstrapper`, which:

1. Checks three places for an existing `ffmpeg.exe`: next to the app, in a per-user
   cache folder (`%LocalAppData%\Voxa\ffmpeg\`), and on the system `PATH`.
2. If none is found, downloads a static Windows build (trying
   **gyan.dev**, then the **BtbN GitHub release** as a fallback mirror if that fails)
   and caches it in that `%LocalAppData%` folder, showing real download progress.
3. Opens `MainWindow` once a working copy is confirmed in place.

`FFmpegService` (the class that actually runs conversions) checks the exact same three
locations, in the same order, so whatever `SetupWindow` found or installed is what
processing uses.

**If you'd rather ship FFmpeg pre-bundled** (e.g. for a fully offline installer, or to
avoid any first-run download on a locked-down machine), you still can — place
`ffmpeg.exe` at `Voxa/ffmpeg/ffmpeg.exe` before building:

1. Go to **https://www.gyan.dev/ffmpeg/builds/** (or **https://github.com/BtbN/FFmpeg-Builds/releases**).
2. Download an **essentials** or **release, static, win64** build (you only need
   `ffmpeg.exe`).
3. Extract it and copy `ffmpeg.exe` into `Voxa/ffmpeg/`, plus the
   build's `LICENSE`/`COPYING` file as `LICENSE.txt` — see the licensing note in section 6.

With a bundled copy present, the bootstrapper finds it immediately and never touches
the network at all, even on a brand-new machine.

**If a machine has no internet access and no bundled copy**, `SetupWindow` shows a
clear error with Retry/Quit — it never leaves the user staring at a frozen or silently
broken app.

## 3. Build & run locally

Requires the **.NET 8 SDK** (Windows). WPF only runs on Windows, so this must be
built and run on a Windows machine — it will not build in a Linux/macOS environment.

```powershell
cd Voxa
dotnet restore
dotnet run
```

## 4. What each screen control does

| Section | Control | Behavior |
|---|---|---|
| File queue | Add Files / Add Folder / drag-and-drop | Adds individual files or every supported audio file in a folder (recursively). Duplicates and unsupported extensions are skipped with a status message. |
| File queue | Remove Selected / Clear All | Only enabled when not currently processing. |
| Output format | Dropdown | mp3, wav, m4a, flac, ogg, aac |
| Sample rate | Checkbox + dropdown | "Keep original" leaves each file's native rate untouched; otherwise pick or type a value in Hz (8,000–192,000). |
| Volume | Slider (-30..+30 dB) + Normalize checkbox | Manual gain, and/or EBU R128 loudness normalization to even out volume across a batch. |
| Clarity | Checkbox | Light denoise (`afftdn`) + rumble cut (`highpass`) + a gentle presence boost — aimed at muffled or noisy speech recordings. |
| Playback speed | Slider (0.5x–2.0x) | Pitch-preserving speed change (`atempo`). |
| Presets | Dropdown + Save/Delete | Selecting a preset from the dropdown applies it immediately. Type a name and click **Save Preset** to store the current settings; **Delete Selected Preset** removes the one currently selected. |
| Output folder | Browse / Open | Where processed files are written. Never the same file as — and never overwrites — an original. |
| Footer | Start Processing / Cancel | Runs the batch on a background task (UI stays responsive); Cancel finishes the file in progress, then stops. |

## 5. How processing works (for reference)

For each file, `FFmpegService`:

1. Probes duration via `ffmpeg -i <file>` (reads the `Duration:` line from stderr).
2. Runs the real conversion with `-progress pipe:1`, so FFmpeg streams
   `out_time_ms=...` lines that get turned into a 0–100% value for that file's
   progress bar in the UI.
3. Builds a single `-af` filter chain in a fixed, sensible order: clarity filters →
   manual volume → loudness normalization → speed (`atempo`, auto-chained for
   speeds outside 0.5x–2.0x since a single `atempo` stage can't go beyond that).
4. Maps the output extension to the right codec (`libmp3lame` for mp3, `aac` for
   m4a/aac, `pcm_s16le` for wav, `flac`, `libvorbis` for ogg).
5. If the output filename would collide with an existing file (e.g. two different
   source files converting to the same name), a `_1`, `_2`, … suffix is added
   automatically — nothing is ever overwritten.
6. A non-zero exit code or a missing output file is reported as a per-file failure
   with FFmpeg's own error line surfaced in the **Details** column; the batch
   continues with the next file regardless.

Presets are stored as JSON at `%AppData%\Voxa\presets.json` and
persist across app restarts and updates (same Windows user profile).

## 6. Packaging as a standalone Windows app

Because FFmpeg no longer has to be bundled (see section 2), packaging is simpler
than before. **Option A is recommended for your friend's use case** — a small
download, and the app fetches FFmpeg itself the first time it's opened.

### Option A — self-contained folder (recommended)

Easiest: double-click **`publish.bat`** in the project root. It runs the command
below for you and opens the resulting folder when it's done.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

This produces a `publish/` folder containing `Voxa.exe` and the .NET
runtime files (plus `ffmpeg/ffmpeg.exe` too, if you placed one there per section 2).
Zip the whole `publish` folder and send it to your friend — they unzip it anywhere
and double-click `Voxa.exe`. No .NET installation, no admin rights,
no command line needed on their end. The **first launch only** needs an internet
connection (to fetch FFmpeg, a one-time ~25–70 MB download); every launch after
that works fully offline.

### Option B — single .exe file

```powershell
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeAllContentForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o publish
```

Bundles the app and .NET runtime into one `Voxa.exe`. If you also
placed `ffmpeg.exe` under `ffmpeg/` per section 2, that gets embedded too and
extracted to a temp folder on first run each session (`FFmpegService` already
looks relative to `AppContext.BaseDirectory`, so no code changes are needed) —
otherwise the app just downloads FFmpeg on first launch as usual.

### Option C — a real installer (VoxaSetup.exe)

For a polished install experience — Start Menu shortcut, optional Desktop shortcut,
proper uninstaller entry in "Add or Remove Programs" — use the Inno Setup script in
`installer/`. This is the recommended option if you're distributing to more than one
or two people, since it's the most familiar/expected format for a Windows app.

**One-time setup:** install Inno Setup 6 (free) from https://jrsoftware.org/isdl.php.
Default install location is fine — the build script looks for it there automatically.

**Every time you want to build the installer:** double-click
**`installer\build-installer.bat`**. It runs the same `dotnet publish` as Option A,
then feeds the result into Inno Setup automatically. When it finishes, you'll have:

```
installer\Output\VoxaSetup.exe
```

That's the one file to send your friend. They double-click it, click through a normal
Next → Next → Install wizard (no technical choices), and Voxa shows up in their Start
Menu — plus their Desktop, if they leave the "create a desktop shortcut" box checked.
No admin rights are required (`installer/Voxa.iss` sets `PrivilegesRequired=lowest`,
so it installs to the current user's own profile), so this also works on locked-down
work laptops where Option A/B's manual unzip-and-run might be blocked by policy but a
signed-feeling installer flow generally isn't.

Uninstalling removes the installed app files. It does **not** delete the cached FFmpeg
copy at `%LocalAppData%\Voxa\ffmpeg` — that's intentional, so a reinstall later skips
the first-run download again. If someone wants a fully clean removal, they can delete
that folder by hand.

If you change the app version, update it in all three places so they don't drift:
`Voxa.csproj` (`AssemblyVersion`/`FileVersion`), `Services/UpdateChecker.cs`
(the `AppVersion.Current` value near the bottom of the file, used for the in-app
version label and the update-check comparison), and `installer/Voxa.iss`
(`MyAppVersion`).

### Optional: a background update check

`Services/UpdateChecker.cs` does a quiet, one-time check against a GitHub repo's
Releases API when the app starts, and shows a small dismissible "Update available"
banner (with a **View** button that opens the release page) if a newer tagged release
is found. It's entirely best-effort: no internet, GitHub being unreachable, or a
missing/renamed repo all just mean no banner appears — never an error, never a delay
opening the app.

Before shipping, set `RepoOwner` and `RepoName` at the top of `UpdateChecker.cs` to
your actual GitHub repository, and tag releases there like `v1.1.0` (a leading `v` is
handled automatically). If you don't use GitHub Releases, you can safely ignore this
feature — leaving the placeholder repo name in place just means the check always
fails silently and no banner ever shows, which is a safe do-nothing default.

### Licensing note on redistributing FFmpeg

*(Only relevant if you choose to bundle `ffmpeg.exe` yourself per section 2 — the
automatic first-run download doesn't redistribute anything, it just points the
user's own machine at the official FFmpeg download servers.)*

FFmpeg is licensed LGPL or GPL depending on which build/configuration you use.
If you redistribute `ffmpeg.exe`:
- Prefer an **LGPL** build (the gyan.dev/BtbN pages label this) unless you're fine
  with GPL obligations.
- Include the FFmpeg license text with your distribution (see step 2.4 above).
- This is a legal/compliance matter, not a coding one — review the license that
  ships with whichever build you download.

## 7. Suggested test checklist before shipping

- [ ] On a clean Windows VM/machine with **no FFmpeg anywhere**, launch the app and
      confirm the setup screen appears, shows real download progress, and hands off
      to the main window automatically when done.
- [ ] Close and reopen the app on that same machine — confirm setup is now instant
      (it found the cached copy at `%LocalAppData%\Voxa\ffmpeg\`).
- [ ] Disconnect from the internet on a machine with no cached/bundled FFmpeg and
      launch the app — confirm the setup screen shows a clear error with working
      Retry and Quit buttons, rather than hanging or crashing.
- [ ] Convert a small mp3 → wav and confirm the original mp3 is untouched.
- [ ] Drag-and-drop a folder containing a mix of supported and unsupported files.
- [ ] Set sample rate to 16000 Hz and confirm the output file's rate with
      `ffmpeg -i output.wav` (or any media info tool).
- [ ] Save a preset, restart the app, confirm it's still in the dropdown.
- [ ] Start a batch of 10+ files, click Cancel partway through, confirm the
      in-progress file is skipped/cancelled and the rest are left "Waiting".
- [ ] Point the app at a corrupted/non-audio file mixed into a batch — confirm it
      fails gracefully with a message and the rest of the batch still completes.
- [ ] Run the packaged (published) build on a clean Windows machine with no .NET
      or FFmpeg installed.
- [ ] Run `installer\build-installer.bat` and confirm it produces
      `installer\Output\VoxaSetup.exe` without errors.
- [ ] Run `VoxaSetup.exe` on a clean machine with a **non-admin** Windows account —
      confirm it installs without asking for elevation, adds Start Menu and (if
      checked) Desktop shortcuts, and Voxa launches correctly afterward.
- [ ] Uninstall via "Add or Remove Programs" and confirm the app files are removed
      while `%LocalAppData%\Voxa\ffmpeg` is left alone (intentional — see section 6).
- [ ] With `RepoOwner`/`RepoName` in `UpdateChecker.cs` pointed at a real repo that
      has an older tagged release than `AppVersion.Current`, confirm the update
      banner appears and its **View** button opens the right release page.

## 8. Ideas for later extension

- Per-file custom output filenames / a rename pattern.
- A "preview" (play first few seconds with current settings applied).
- Additional formats (opus, wma) if a use case needs them — just add codec
  mappings in `FFmpegService.CodecArgumentsFor`.
- Code-signing the installer/exe, so Windows SmartScreen doesn't warn first-time
  users on download — needs a code-signing certificate, which is a paid step
  outside what this project sets up.
- Auto-update (actually downloading and applying a newer version), rather than
  today's "notify and link to the release page" — a bigger undertaking, since it
  means safely replacing a running app's own files.
