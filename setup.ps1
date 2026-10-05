#!/usr/bin/env pwsh
<#
.SYNOPSIS
Setup script for Whisper Dictation Phase 1
.DESCRIPTION
Restores NuGet packages and builds the solution.
#>

Write-Host "=== Whisper Dictation - Phase 1 Setup ===" -ForegroundColor Cyan

$SolutionPath = "$PSScriptRoot\WhisperDictation.sln"

if (-not (Test-Path $SolutionPath)) {
    Write-Error "Solution file not found: $SolutionPath"
    exit 1
}

Write-Host "`n[1/3] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore $SolutionPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "Package restore failed"
    exit 1
}

Write-Host "`n[2/3] Building solution..." -ForegroundColor Yellow
dotnet build $SolutionPath --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

Write-Host "`n[3/3] Running tests..." -ForegroundColor Yellow
dotnet test $SolutionPath --configuration Release --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Tests failed or no tests exist yet"
}

Write-Host "`n=== Setup Complete ===" -ForegroundColor Green
Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "  1. Open WhisperDictation.sln in Visual Studio" -ForegroundColor White
Write-Host "  2. Review AudioCaptureService.cs implementation" -ForegroundColor White
Write-Host "  3. Phase 1.3 will add console app for testing" -ForegroundColor White
Write-Host "`n"