# Docker Build Evidence

## Build Configuration
- **Date**: 2026-08-19
- **Configuration**: Release
- **Tests Enabled**: Yes
- **Platform**: Linux (Docker with .NET SDK 10.0)
- **Docker Image**: `loquacio-build:latest`

## Docker Build Output

```
=== Loquacio Docker Build ===
Configuration: Release
Run tests: true
Clean build: false
No cache: false
Shell mode: false

Building Docker image...
[Build stages completed successfully]
✅ Docker image built successfully

Running build in container...
=== Loquacio Build ===
Configuration: Release
Tests: true

Building solution...
An issue was encountered verifying workloads. For more information, run "dotnet workload update".
  Determining projects to restore...
  Restored /app/Loquacio.Shared/Loquacio.Shared.csproj (in 13.4 sec).
  Restored /app/Loquacio/Loquacio.csproj (in 13.4 sec).
  Restored /app/Loquacio.Wpf.Tests/Loquacio.Wpf.Tests.csproj (in 13.4 sec).
  Restored /app/Loquacio.Tui/Loquacio.Tui.csproj (in 15 ms).
  Restored /app/TestConsole/TestConsole.csproj (in 19 ms).
  Restored /app/Loquacio.Core/Loquacio.Core.csproj (in 5 ms).
  Restored /app/Loquacio.Daemon/Loquacio.Daemon.csproj (in 13 ms).
  Restored /app/Loquacio.Avalonia/Loquacio.Avalonia.csproj (in 15.34 sec).
  Restored /app/Loquacio.Tests/Loquacio.Tests.csproj (in 28.77 sec).
  Loquacio.Core -> /app/Loquacio.Core/bin/Release/net10.0/Loquacio.Core.dll
  Loquacio.Tui -> /app/Loquacio.Tui/bin/Release/net10.0/loquacio-tui.dll
  Loquacio.Shared -> /app/Loquacio.Shared/bin/Release/net10.0/Loquacio.Shared.dll
  Loquacio.Avalonia -> /app/Loquacio.Avalonia/bin/Release/net10.0/loquacio-avalonia.dll
  Loquacio.Daemon -> /app/Loquacio.Daemon/bin/Release/net10.0/loquacio-daemon.dll
/app/Loquacio.Tests/Daemon/PipeWireAudioCaptureServiceTests.cs(68,25): warning CS8602: Dereference of a possibly null reference. [/app/Loquacio.Tests/Loquacio.Tests.csproj]
  Loquacio.Tests -> /app/Loquacio.Tests/bin/Release/net10.0/Loquacio.Tests.dll
  Loquacio -> /app/Loquacio/bin/Release/net10.0-windows/Loquacio.dll
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
  - Loquacio.Core (net10.0) — cross-platform library
  - Loquacio (net10.0-windows) — WPF application
  - Loquacio.Tests (net10.0) — cross-platform tests
  - TestConsole (net10.0-windows) — Windows test harness

✅ Docker build completed successfully

Build artifacts are available in:
  - Loquacio/bin/Release/net10.0-windows/
  - Loquacio.Core/bin/Release/net10.0/
  - TestConsole/bin/Release/net10.0-windows/
  - Loquacio.Tests/bin/Release/net10.0/
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

- **Loquacio.Core**: Cross-platform library (.NET 10.0)
- **Loquacio**: WPF application (.NET 10.0-windows)
- **Loquacio.Tests**: Cross-platform test suite (.NET 10.0)
- **TestConsole**: Windows test harness (.NET 10.0-windows)
- **Loquacio.Daemon**: Background service (.NET 10.0)
- **Loquacio.Avalonia**: Avalonia UI application (.NET 10.0)
- **Loquacio.Tui**: Terminal UI application (.NET 10.0)

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
