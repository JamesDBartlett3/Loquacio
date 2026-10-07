#!/usr/bin/env pwsh
# Build portable Windows package for loquacio (Phase D task 2).
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
$App = "loquacio"

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
    dotnet publish Loquacio.Engine/Loquacio.Engine.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$NuggetVersion -o $Publish
    if ($LASTEXITCODE -ne 0) { Write-Error "Publishing Loquacio.Engine failed"; exit 1 }
    dotnet publish Loquacio/Loquacio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$NuggetVersion -o $Publish
    if ($LASTEXITCODE -ne 0) { Write-Error "Publishing Loquacio (WPF) failed"; exit 1 }
    dotnet publish Loquacio.Tui/Loquacio.Tui.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$NuggetVersion -o $Publish
    if ($LASTEXITCODE -ne 0) { Write-Error "Publishing Loquacio.Tui failed"; exit 1 }
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
start "" "Loquacio.exe"
"@ | Out-File -FilePath "$Stage\Start Loquacio.bat" -Encoding ASCII

# Create Task Scheduler installation script
@"
@echo off
echo Installing Loquacio engine as scheduled task...
schtasks /create /tn "Loquacio Engine" /tr "%~dp0loquacio-engine.exe" /sc onlogon /rl highest /f
if %errorlevel% equ 0 (
    echo Success! Engine will start automatically on login.
) else (
    echo Failed to create scheduled task. Run as administrator?
)
pause
"@ | Out-File -FilePath "$Stage\Install Engine.bat" -Encoding ASCII

# Create uninstallation script
@"
@echo off
echo Uninstalling Loquacio engine...
schtasks /delete /tn "Loquacio Engine" /f
if %errorlevel% equ 0 (
    echo Success! Engine will not start automatically.
) else (
    echo Task may not have been installed.
)
pause
"@ | Out-File -FilePath "$Stage\Uninstall Engine.bat" -Encoding ASCII

# Create README for portable package
@"
# Loquacio - Portable Windows Package

## Quick Start

1. Double-click \`Start Loquacio.bat\` to launch the GUI controller.
2. The engine will start automatically when needed.

## Automatic Startup

To have the engine start automatically when you log in:
- Right-click \`Install Engine.bat\` and select "Run as administrator"
- The engine will start on your next login

To remove automatic startup:
- Right-click \`Uninstall Engine.bat\` and select "Run as administrator"

## Components

- \`Loquacio.exe\` - GUI controller (Windows Presentation Foundation)
- \`loquacio-engine.exe\` - Background engine (audio capture + transcription)
- \`loquacio-tui.exe\` - Terminal UI controller (optional)

## Configuration

Settings are stored in:
- \`%APPDATA%\Loquacio\settings.json\`

Models are downloaded to:
- \`%LOCALAPPDATA%\Loquacio\models\`

## Troubleshooting

If the engine doesn't start:
- Check Windows Event Viewer → Windows Logs → Application
- Ensure no firewall is blocking the engine
- Verify audio device is available in Settings → Audio tab

## Version

This is Loquacio version $Version
Built from commit: $ShortHash
"@ | Out-File -FilePath "$Stage\README-Windows.md" -Encoding UTF8

# Create ZIP archive
$ZipPath = "$Dist/${App}-${Version}-win-x64-portable.zip"
Compress-Archive -Path "$Stage\*" -DestinationPath $ZipPath -Force

Write-Host "✅ Portable package built: $ZipPath"
Write-Host ""
Write-Host "To install:"
Write-Host "  1. Extract the ZIP to a folder of your choice"
Write-Host "  2. Run 'Start Loquacio.bat' to launch"
Write-Host "  3. (Optional) Run 'Install Engine.bat' as admin for auto-startup"
