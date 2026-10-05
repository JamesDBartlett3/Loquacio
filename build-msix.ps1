# Loquacio — MSIX Build Script
# Run on Windows in PowerShell (not PowerShell Core on Linux)
# Requires: .NET 10 SDK, Windows SDK, Windows App SDK

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [switch]$Sign,
    [string]$CertPath = "",
    [string]$CertPassword = ""
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "Building Loquacio MSIX ($Configuration, $Platform)..." -ForegroundColor Cyan

# 1. Restore and build the app
Write-Host "`n[1/4] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore Loquacio.slnx
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

# 2. Build the WPF app
Write-Host "`n[2/4] Building WPF application..." -ForegroundColor Yellow
dotnet build Loquacio/Loquacio.csproj -c $Configuration -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# 3. Build the MSIX package
Write-Host "`n[3/4] Building MSIX package..." -ForegroundColor Yellow
# Use MSBuild (not dotnet build) for the WAP project — it needs Windows SDK targets
$msbuild = & "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" 2>$null
if (-not $msbuild) {
    $msbuild = & "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" 2>$null
}
if (-not $msbuild) {
    $msbuild = & "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" 2>$null
}
if (-not $msbuild) {
    # Fall back to dotnet build (works if Windows App SDK targets are installed)
    dotnet build Loquacio.Package.wapproj -c $Configuration -p:Platform=$Platform
} else {
    & $msbuild Loquacio.Package.wapproj /p:Configuration=$Configuration /p:Platform=$Platform /p:AppxBundlePlatforms="$Platform" /p:AppxPackageDir="$PSScriptRoot\MSIXOutput" /p:GenerateAppxPackageOnBuild=true
}
if ($LASTEXITCODE -ne 0) { throw "MSIX build failed" }

# 4. Locate the output
Write-Host "`n[4/4] Locating MSIX output..." -ForegroundColor Yellow
$msixFiles = Get-ChildItem -Path "MSIXOutput", "AppPackages" -Recurse -Filter "*.msix" -ErrorAction SilentlyContinue
if ($msixFiles) {
    Write-Host "`nMSIX package(s) created:" -ForegroundColor Green
    $msixFiles | ForEach-Object {
        Write-Host "  $($_.FullName) ($([math]::Round($_.Length / 1MB, 1)) MB)" -ForegroundColor Green
    }

    # Optional signing
    if ($Sign -and $CertPath -and (Test-Path $CertPath)) {
        Write-Host "`nSigning MSIX..." -ForegroundColor Yellow
        $signtool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" | Select-Object -Last 1
        if ($signtool) {
            & $signtool.FullName sign /fd SHA256 /a /f $CertPath /p $CertPassword $msixFiles[0].FullName
            Write-Host "Signed: $($msixFiles[0].FullName)" -ForegroundColor Green
        } else {
            Write-Warning "signtool.exe not found. Install Windows SDK to sign MSIX packages."
        }
    }
} else {
    Write-Warning "No .msix files found. Check build output above."
}

Write-Host "`nDone." -ForegroundColor Cyan
