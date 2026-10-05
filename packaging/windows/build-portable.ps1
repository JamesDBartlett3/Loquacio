#!/usr/bin/env pwsh
# Build portable Windows package for whisper-dictation (Phase D task 2).
# Usage:
#   packaging/windows/build-portable.ps1 [--skip-build]

param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
Set-Location $RepoRoot

$Dist = "$RepoRoot/dist"
$Publish = "$Dist/publish/win-x64"
$App = "whisper-dictation"

# Get version from git (strip the "v" prefix — it is not a valid NuGet version)
$Base = & git describe --tags --abbrev=0 2>$null
if (-not $Base) { $Base = "0.0.0" }
$Base = $Base.TrimStart("v", "V")
$ShortHash = & git rev-parse --short HEAD
$Version = "$Base.git$ShortHash"
$NuggetVersion = "$Base+$ShortHash"

Write-Host "Building Windows portable package version $Version..."

# Build the binaries
if (-not $SkipBuild) {
    Write-Host "Publishing binaries..."
    dotnet publish WhisperDictation.Daemon/WhisperDictation.Daemon.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$NuggetVersion -o $Publish
    if ($LASTEXITCODE -ne 0) { Write-Error "Publishing WhisperDictation.Daemon failed"; exit 1 }
    dotnet publish WhisperDictation/WhisperDictation.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$NuggetVersion -o $Publish
    if ($LASTEXITCODE -ne 0) { Write-Error "Publishing WhisperDictation (WPF) failed"; exit 1 }
    dotnet publish WhisperDictation.Tui/WhisperDictation.Tui.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$NuggetVersion -o $Publish
    if ($LASTEXITCODE -ne 0) { Write-Error "Publishing WhisperDictation.Tui failed"; exit 1 }
}

# Create portable archive structure
$Stage = "$Dist/stage-portable/$App"
Remove-Item -Path $Stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $Stage -Force | Out-Null

# Copy published files
Copy-Item -Path "$Publish\*" -Destination $Stage -Recurse -Force

# Copy documentation
Copy-Item -Path "README.md" -Destination $Stage -ErrorAction SilentlyContinue
Copy-Item -Path "LICENSE" -Destination $Stage -ErrorAction SilentlyContinue

# Create start script
@"
@echo off
cd /d "%~dp0"
start "" "WhisperDictation.exe"
"@ | Out-File -FilePath "$Stage\Start Whisper Dictation.bat" -Encoding ASCII

# Create Task Scheduler installation script
@"
@echo off
echo Installing Whisper Dictation daemon as scheduled task...
schtasks /create /tn "WhisperDictation Daemon" /tr "%~dp0whisper-dictation-daemon.exe" /sc onlogon /rl highest /f
if %errorlevel% equ 0 (
    echo Success! Daemon will start automatically on login.
) else (
    echo Failed to create scheduled task. Run as administrator?
)
pause
"@ | Out-File -FilePath "$Stage\Install Daemon.bat" -Encoding ASCII

# Create uninstallation script
@"
@echo off
echo Uninstalling Whisper Dictation daemon...
schtasks /delete /tn "WhisperDictation Daemon" /f
if %errorlevel% equ 0 (
    echo Success! Daemon will not start automatically.
) else (
    echo Task may not have been installed.
)
pause
"@ | Out-File -FilePath "$Stage\Uninstall Daemon.bat" -Encoding ASCII

# Create README for portable package
@"
# Whisper Dictation - Portable Windows Package

## Quick Start

1. Double-click \`Start Whisper Dictation.bat\` to launch the GUI controller.
2. The daemon will start automatically when needed.

## Automatic Startup

To have the daemon start automatically when you log in:
- Right-click \`Install Daemon.bat\` and select "Run as administrator"
- The daemon will start on your next login

To remove automatic startup:
- Right-click \`Uninstall Daemon.bat\` and select "Run as administrator"

## Components

- \`WhisperDictation.exe\` - GUI controller (Windows Presentation Foundation)
- \`whisper-dictation-daemon.exe\` - Background daemon (audio capture + transcription)
- \`whisper-dictation-tui.exe\` - Terminal UI controller (optional)

## Configuration

Settings are stored in:
- \`%APPDATA%\WhisperDictation\settings.json\`

Models are downloaded to:
- \`%LOCALAPPDATA%\WhisperDictation\models\`

## Troubleshooting

If the daemon doesn't start:
- Check Windows Event Viewer → Windows Logs → Application
- Ensure no firewall is blocking the daemon
- Verify audio device is available in Settings → Audio tab

## Version

This is Whisper Dictation version $Version
Built from commit: $ShortHash
"@ | Out-File -FilePath "$Stage\README-Windows.md" -Encoding UTF8

# Create ZIP archive
$ZipPath = "$Dist/${App}-${Version}-win-x64-portable.zip"
Compress-Archive -Path "$Stage\*" -DestinationPath $ZipPath -Force

Write-Host "✅ Portable package built: $ZipPath"
Write-Host ""
Write-Host "To install:"
Write-Host "  1. Extract the ZIP to a folder of your choice"
Write-Host "  2. Run 'Start Whisper Dictation.bat' to launch"
Write-Host "  3. (Optional) Run 'Install Daemon.bat' as admin for auto-startup"
