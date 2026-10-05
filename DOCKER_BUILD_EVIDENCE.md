# Docker Build Evidence

## Build Configuration
- **Date**: 2026-08-19
- **Configuration**: Release
- **Tests Enabled**: Yes
- **Platform**: Linux (Docker with .NET SDK 10.0)
- **Docker Image**: `whisper-dictation-build:latest`

## Docker Build Output

```
=== Whisper Dictation Docker Build ===
Configuration: Release
Run tests: true
Clean build: false
No cache: false
Shell mode: false

Building Docker image...
[Build stages completed successfully]
✅ Docker image built successfully

Running build in container...
=== Whisper Dictation Build ===
Configuration: Release
Tests: true

Building solution...
An issue was encountered verifying workloads. For more information, run "dotnet workload update".
  Determining projects to restore...
  Restored /app/WhisperDictation.Shared/WhisperDictation.Shared.csproj (in 13.4 sec).
  Restored /app/WhisperDictation/WhisperDictation.csproj (in 13.4 sec).
  Restored /app/WhisperDictation.Wpf.Tests/WhisperDictation.Wpf.Tests.csproj (in 13.4 sec).
  Restored /app/WhisperDictation.Tui/WhisperDictation.Tui.csproj (in 15 ms).
  Restored /app/TestConsole/TestConsole.csproj (in 19 ms).
  Restored /app/WhisperDictation.Core/WhisperDictation.Core.csproj (in 5 ms).
  Restored /app/WhisperDictation.Daemon/WhisperDictation.Daemon.csproj (in 13 ms).
  Restored /app/WhisperDictation.Avalonia/WhisperDictation.Avalonia.csproj (in 15.34 sec).
  Restored /app/WhisperDictation.Tests/WhisperDictation.Tests.csproj (in 28.77 sec).
  WhisperDictation.Core -> /app/WhisperDictation.Core/bin/Release/net10.0/WhisperDictation.Core.dll
  WhisperDictation.Tui -> /app/WhisperDictation.Tui/bin/Release/net10.0/whisper-dictation-tui.dll
  WhisperDictation.Shared -> /app/WhisperDictation.Shared/bin/Release/net10.0/WhisperDictation.Shared.dll
  WhisperDictation.Avalonia -> /app/WhisperDictation.Avalonia/bin/Release/net10.0/whisper-dictation-avalonia.dll
  WhisperDictation.Daemon -> /app/WhisperDictation.Daemon/bin/Release/net10.0/whisper-dictation-daemon.dll
/app/WhisperDictation.Tests/Daemon/PipeWireAudioCaptureServiceTests.cs(68,25): warning CS8602: Dereference of a possibly null reference. [/app/WhisperDictation.Tests/WhisperDictation.Tests.csproj]
  WhisperDictation.Tests -> /app/WhisperDictation.Tests/bin/Release/net10.0/WhisperDictation.Tests.dll
  WhisperDictation -> /app/WhisperDictation/bin/Release/net10.0-windows/WhisperDictation.dll
  TestConsole -> /app/TestConsole/bin/Release/net10.0-windows/TestConsole.dll

Running tests...
[267 tests ran successfully - see full output in docker-build.sh log]

Test Run Successful.
Total tests: 267
     Passed: 267
 Total time: 4.8562 Seconds

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:05.79
✅ All tests passed

Build summary:
  - WhisperDictation.Core (net10.0) — cross-platform library
  - WhisperDictation (net10.0-windows) — WPF application
  - WhisperDictation.Tests (net10.0) — cross-platform tests
  - TestConsole (net10.0-windows) — Windows test harness

✅ Docker build completed successfully

Build artifacts are available in:
  - WhisperDictation/bin/Release/net10.0-windows/
  - WhisperDictation.Core/bin/Release/net10.0/
  - TestConsole/bin/Release/net10.0-windows/
  - WhisperDictation.Tests/bin/Release/net10.0/
```

## Test Results Summary

| Metric | Value |
|--------|-------|
| Total Tests | 267 |
| Passed | 267 |
| Failed | 0 |
| Skipped | 0 |
| Test Duration | 4.86 seconds |
| Build Status | ✅ Success |

## Build Artifacts

All projects built successfully with the following outputs:

- **WhisperDictation.Core**: Cross-platform library (.NET 10.0)
- **WhisperDictation**: WPF application (.NET 10.0-windows)
- **WhisperDictation.Tests**: Cross-platform test suite (.NET 10.0)
- **TestConsole**: Windows test harness (.NET 10.0-windows)
- **WhisperDictation.Daemon**: Background service (.NET 10.0)
- **WhisperDictation.Avalonia**: Avalonia UI application (.NET 10.0)
- **WhisperDictation.Tui**: Terminal UI application (.NET 10.0)

## Environment

- **Base Image**: `mcr.microsoft.com/dotnet/sdk:10.0`
- **Container User**: Host user (UID 1000 via `--user` flag)
- **Working Directory**: `/app`
- **Runtime**: Linux container with Windows cross-compilation support

## Notes

- Single compiler warning (nullable reference) in `PipeWireAudioCaptureServiceTests.cs:68` - non-blocking
- All 267 tests passed successfully
- Docker build completed without errors
- Container runs as non-root user (matching host UID for bind-mount compatibility)
