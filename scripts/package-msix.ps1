#Requires -Version 5.1
# Loquacio — MSIX packaging for the Avalonia desktop app.
# MUST run on a Windows host with: .NET 10 SDK + Windows 10/11 SDK (MakeAppx.exe, signtool.exe).
# (The Linux dev host cannot build MSIX packages; this script is the documented Windows path.)

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0.0",
    [string]$OutputDir = "",
    [switch]$SelfContained,
    [switch]$Sign,
    [string]$CertPath = "",
    [string]$CertPassword = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$OutDir = if ($OutputDir) { $OutputDir } else { Join-Path $RepoRoot "dist\msix" }
$Staging = Join-Path $OutDir "staging-$Runtime"
$ManifestSrc = Join-Path $RepoRoot "packaging\windows\msix\AppxManifest.xml"
$AssetsSrc = Join-Path $RepoRoot "packaging\windows\msix\assets"

Write-Host "Loquacio MSIX packaging ($Configuration / $Runtime)..." -ForegroundColor Cyan

# 1. Publish the Avalonia app
Write-Host "[1/4] Publishing loquacio-avalonia..." -ForegroundColor Yellow
$publishArgs = @(
    "publish", (Join-Path $RepoRoot "Loquacio.Avalonia\Loquacio.Avalonia.csproj"),
    "-c", $Configuration, "-r", $Runtime,
    "-p:Version=$Version",
    "-o", $Staging
)
if ($SelfContained) { $publishArgs += "--self-contained" }
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 2. Stage manifest + assets alongside the app payload
Write-Host "[2/4] Staging manifest and assets..." -ForegroundColor Yellow
Copy-Item $ManifestSrc (Join-Path $Staging "AppxManifest.xml") -Force
New-Item -ItemType Directory -Force -Path (Join-Path $Staging "assets") | Out-Null
Copy-Item (Join-Path $AssetsSrc "*") (Join-Path $Staging "assets") -Force

# 3. MakeAppx pack
Write-Host "[3/4] Building MSIX with MakeAppx..." -ForegroundColor Yellow
$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter "MakeAppx.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "x64" } | Select-Object -Last 1
if (-not $makeappx) { throw "MakeAppx.exe not found. Install the Windows 10/11 SDK." }
$msixPath = Join-Path $OutDir "Loquacio-$Version-$Runtime.msix"
& $makeappx.FullName pack /o /d $Staging /p $msixPath
if ($LASTEXITCODE -ne 0) { throw "MakeAppx pack failed" }

# 4. Optional signing
Write-Host "[4/4] Post-build..." -ForegroundColor Yellow
if ($Sign) {
    if (-not (Test-Path $CertPath)) { throw "Signing requested but CertPath not found: $CertPath" }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "x64" } | Select-Object -Last 1
    if (-not $signtool) { throw "signtool.exe not found. Install the Windows SDK." }
    & $signtool.FullName sign /fd SHA256 /a /f $CertPath /p $CertPassword $msixPath
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
    Write-Host "Signed: $msixPath" -ForegroundColor Green
} else {
    Write-Warning "Package is UNSIGNED. Install via Add-AppxPackage only with a trusted cert, or pass -Sign."
}

Write-Host "Done: $msixPath" -ForegroundColor Green

