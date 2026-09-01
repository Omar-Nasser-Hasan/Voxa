@echo off
setlocal

echo ============================================
echo   Voxa - Build the Windows installer
echo ============================================
echo.
echo This does two things in one go:
echo   1. Publishes Voxa as a self-contained app (like publish.bat)
echo   2. Compiles that into a single VoxaSetup.exe your friend can
echo      double-click - no .NET, no FFmpeg, nothing else needed on
echo      their machine.
echo.
echo Only YOUR machine needs the .NET SDK and Inno Setup 6 to build
echo this. Your friend never needs either.
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK not found on this machine.
    echo Install it from: https://dotnet.microsoft.com/download/dotnet/8.0
    echo ^(choose the SDK, not just the Runtime^), then run this script again.
    echo.
    pause
    exit /b 1
)

REM ISCC.exe (the Inno Setup command-line compiler) isn't normally on PATH,
REM so check the two locations the standard installer uses before giving up.
set "ISCC="
where ISCC.exe >nul 2>nul
if not errorlevel 1 (
    set "ISCC=ISCC.exe"
) else if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
    set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
) else if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" (
    set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
)

if not defined ISCC (
    echo [ERROR] Inno Setup 6 not found.
    echo Install it ^(free^) from: https://jrsoftware.org/isinfo.php
    echo then run this script again.
    echo.
    pause
    exit /b 1
)

REM This script lives in installer\, but the project (and publish.bat's
REM working assumptions) live one folder up - step out so the publish
REM output lands at ..\publish, exactly where Voxa.iss's PublishDir expects it.
pushd "%~dp0.."

echo Publishing a self-contained copy...
echo.

dotnet publish -c Release -r win-x64 --self-contained true -o publish

if errorlevel 1 (
    echo.
    echo [ERROR] Build failed - see the messages above.
    popd
    pause
    exit /b 1
)

echo.
echo Publish complete. Compiling the installer...
echo.

"%ISCC%" "installer\Voxa.iss"

if errorlevel 1 (
    echo.
    echo [ERROR] Inno Setup compilation failed - see the messages above.
    popd
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Done!
echo ============================================
echo VoxaSetup.exe is in installer\Output\ - that's the single file
echo you send your friend. They double-click it, click through the
echo installer, and Voxa is on their Start Menu. Nothing else to
echo install on their end - FFmpeg is bundled into the installer.
echo.

explorer installer\Output
popd
pause
