@echo off
setlocal

echo ============================================
echo   Voxa - Build for a friend
echo ============================================
echo.
echo This creates a folder your friend can run with ZERO installs -
echo no .NET, no FFmpeg, nothing. Only YOUR machine needs the .NET SDK
echo to build it; your friend never needs it.
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

echo Building a self-contained copy...
echo.

dotnet publish -c Release -r win-x64 --self-contained true -o publish

if errorlevel 1 (
    echo.
    echo [ERROR] Build failed - see the messages above.
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Done!
echo ============================================
echo The "publish" folder that just opened is what you send your friend.
echo They unzip it anywhere and double-click Voxa.exe.
echo Nothing else to install on their end - FFmpeg still sets itself up
echo automatically the first time THEY open it, same as before.
echo.

explorer publish
pause
