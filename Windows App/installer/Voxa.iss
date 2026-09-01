; ============================================================================
;  Voxa - Inno Setup installer script
; ============================================================================
;  What this produces: a single VoxaSetup.exe that a non-technical person can
;  double-click. It installs Voxa into Program Files, adds a Start Menu
;  shortcut (and optionally a Desktop shortcut), registers a proper
;  uninstaller in "Add or Remove Programs", and can launch Voxa right after
;  install finishes.
;
;  Prerequisite: the app must already be published as a self-contained folder
;  before running this script - see build-installer.bat in this same folder,
;  which does both steps (publish, then compile this script) in one go.
;
;  Requires Inno Setup 6 (free): https://jrsoftware.org/isinfo.php
;  Open this file in the Inno Setup Compiler (or run ISCC.exe Voxa.iss) after
;  the publish step has produced ..\publish\Voxa.exe.
; ============================================================================

#define MyAppName "Voxa"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Voxa"
#define MyAppExeName "Voxa.exe"
#define MyAppURL ""
#define PublishDir "..\publish"

[Setup]
; A fixed AppId keeps upgrades clean (Inno Setup uses this GUID, not the name,
; to recognize "this is the same app" across versions) - generate your own
; with Tools > Generate GUID in the Inno Setup IDE and keep it forever.
AppId={{5C6E1A2B-6F3D-4B7E-9C2A-8D1F4E7A9B10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; Per-machine by default (Program Files, needs admin once at install time) so
; every user on a shared PC can launch it from the Start Menu. Switch both
; lines below to build a per-user installer instead (no admin prompt, installs
; to the current user's AppData\Local\Programs instead):
;   PrivilegesRequired=lowest
;   DefaultDirName={autopf}\{#MyAppName}  ->  {localappdata}\Programs\{#MyAppName}
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=VoxaSetup
; Points at the source Assets folder (not the publish output) since voxa.ico is
; embedded into Voxa.exe's resources at build time via <ApplicationIcon> in the
; .csproj rather than copied out as a loose file - it's not present in publish\.
SetupIconFile=..\Assets\voxa.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Voxa itself downloads/caches FFmpeg into %LocalAppData%\Voxa on first run,
; and stores presets/history under %AppData%\Voxa - none of that is touched
; here, and uninstalling the app never deletes that user data (see [UninstallDelete]
; note below), so a reinstall or upgrade doesn't lose a user's saved presets.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Pulls in everything dotnet publish produced - the exe, .NET runtime files
; (since publish.bat builds self-contained), and the app's own Assets folder.
; If you also bundled ffmpeg.exe per the README's manual-bundling option,
; it's under publish\ffmpeg\ and gets included automatically here too.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Offers to open Voxa immediately after Setup finishes, same convention as
; most consumer installers. Unchecked would leave it off by default instead.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Deliberately NOT deleting %AppData%\Voxa or %LocalAppData%\Voxa here - that's
; where presets, batch history, and the cached FFmpeg download live. Removing
; the app shouldn't silently destroy a user's saved presets; if they truly
; want a clean wipe they can delete those folders themselves. Only what was
; installed under {app} is removed automatically (handled by Inno by default).
