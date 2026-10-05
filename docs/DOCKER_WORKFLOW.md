# Docker Build & Test Workflow

## Overview

This document describes the Docker-based build and test workflow for the Loquacio project. This workflow enables building Windows-targeting code (including WPF applications) from Linux using Docker containers with the official .NET SDK.

## Why Docker?

The Loquacio project includes:
- **Cross-platform components** (.NET 10.0): Core library, daemon, TUI, Avalonia UI
- **Windows-specific components** (.NET 10.0-windows): WPF application, Windows test harness

By using Docker with the official .NET SDK image, we can:
1. **Build Windows-targeting code from Linux** - Thanks to `EnableWindowsTargeting` in `Directory.Build.props`
2. **Ensure reproducible builds** - Consistent build environment across all machines
3. **Isolate build dependencies** - No need to install .NET SDK on the host system
4. **Run cross-platform tests** - Test suite runs inside the container
5. **Support CI/CD pipelines** - Docker containers integrate easily with automated build systems

## Prerequisites

- Docker 20.10 or later
- Docker Compose (optional, for multi-container workflows)
- At least 4GB RAM available for Docker
- 10GB free disk space for Docker images and build artifacts

## Quick Start

### Basic Build

```bash
./docker-build.sh
```

This builds the entire solution in Release configuration without running tests.

### Build with Tests

```bash
./docker-build.sh --test
```

This builds the solution and runs the test suite.

### Debug Build with Tests

```bash
./docker-build.sh Debug --test
```

This builds the solution in Debug configuration and runs tests.

### Clean Build

```bash
./docker-build.sh --clean --test
```

This removes all build artifacts before building, ensuring a fresh start.

### Rebuild Docker Image

```bash
./docker-build.sh --no-cache --test
```

This rebuilds the Docker image without using cached layers.

### Interactive Shell

```bash
./docker-build.sh --shell
```

This starts an interactive bash shell inside the Docker container for debugging and manual testing.

## Docker Image Details

### Base Image

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0
```

We use the official Microsoft .NET SDK 10.0 image, which includes:
- .NET 10.0 SDK
- All required build tools
- Linux runtime environment

### Build Stages

The Dockerfile uses multi-stage builds:

1. **build stage** - Builds the solution
2. **test stage** - Runs the test suite
3. **final stage** - Sets up entrypoint for running custom commands

### EnableWindowsTargeting

The project uses `EnableWindowsTargeting` in `Directory.Build.props`:

```xml
<PropertyGroup>
  <EnableWindowsTargeting>true</EnableWindowsTargeting>
</PropertyGroup>
```

This allows building Windows-targeting projects on Linux by providing Windows-specific reference assemblies without requiring the Windows SDK.

## Build Artifacts

After a successful build, artifacts are available in:

```
Loquacio/bin/<Configuration>/net10.0-windows/
  ├── Loquacio.dll          # Main WPF application
  ├── Loquacio.exe          # Windows executable
  └── [other dependencies]

Loquacio.Core/bin/<Configuration>/net10.0/
  ├── Loquacio.Core.dll     # Cross-platform core library
  └── [other dependencies]

TestConsole/bin/<Configuration>/net10.0-windows/
  ├── TestConsole.dll               # Windows test harness
  └── [other dependencies]

Loquacio.Tests/bin/<Configuration>/net10.0/
  ├── Loquacio.Tests.dll    # Cross-platform tests
  └── [other dependencies]
```

## Test Execution

### Running Tests

Tests are executed using `dotnet test` inside the container:

```bash
dotnet test Loquacio.Tests/Loquacio.Tests.csproj \
    -c Release --no-build --verbosity normal
```

### Test Coverage

The test suite covers:
- ✅ Core library functionality
- ✅ Daemon services
- ✅ IPC messaging
- ✅ Settings management
- ✅ Model management
- ✅ LLM post-processing
- ✅ Cross-platform components

**Note:** Windows-specific tests (WPF UI, hotkeys, audio capture) are compiled but cannot run on Linux. These are tested separately on Windows machines.

## Troubleshooting

### Docker Build Fails

**Problem:** Docker image build fails with dependency errors.

**Solution:**
```bash
# Clean Docker cache and rebuild
docker system prune -a
./docker-build.sh --no-cache --test
```

### Tests Fail in Container

**Problem:** Tests pass locally but fail in Docker container.

**Solution:**
```bash
# Start shell in container for debugging
./docker-build.sh --shell
# Inside container, run tests manually
dotnet test --verbosity detailed
```

### Out of Memory

**Problem:** Docker container runs out of memory during build.

**Solution:**
1. Increase Docker memory limit in Docker Desktop settings
2. Or build in Release configuration (uses less memory than Debug)

### Permission Issues

**Problem:** Build artifacts have wrong permissions after Docker build.

**Solution:**
```bash
# Fix ownership of build artifacts
sudo chown -R $USER:$USER */bin */obj
```

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Build and Test

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Build and Test
        run: ./docker-build.sh --test

      - name: Upload Test Results
        if: always()
        uses: actions/upload-artifact@v3
        with:
          name: test-results
          path: Loquacio.Tests/TestResults/
```

### GitLab CI Example

```yaml
build-and-test:
  image: docker:latest
  services:
    - docker:dind
  script:
    - ./docker-build.sh --test
  artifacts:
    when: always
    paths:
      - Loquacio.Tests/TestResults/
```

## Performance Tips

1. **Use Release configuration** for faster builds and smaller artifacts
2. **Enable Docker BuildKit** for faster builds:
   ```bash
   DOCKER_BUILDKIT=1 ./docker-build.sh --test
   ```
3. **Cache Docker layers** by avoiding `--no-cache` unless necessary
4. **Parallel builds** - Docker automatically parallelizes multi-stage builds

## Security Considerations

1. **Use official base images** - `mcr.microsoft.com/dotnet/sdk:10.0` is maintained by Microsoft
2. **Scan for vulnerabilities** - Run `docker scan loquacio-build` periodically
3. **Minimize attack surface** - The Dockerfile only includes what's needed for building
4. **Don't run as root in production** - This build container is rootless by default

## Comparison: Native vs Docker Build

| Feature | Native Build | Docker Build |
|---------|--------------|--------------|
| **Speed** | Faster (no container overhead) | Slightly slower (container startup) |
| **Reproducibility** | Depends on host environment | Guaranteed (isolated container) |
| **Dependencies** | Requires .NET SDK on host | Self-contained in image |
| **Windows builds** | Works with EnableWindowsTargeting | Works with EnableWindowsTargeting |
| **CI/CD friendly** | Requires host setup | Works out of the box |
| **Disk space** | Minimal | ~2GB for Docker image |

## Native Build Alternative

If you prefer not to use Docker, the project also supports native Linux builds:

```bash
# Native build (requires .NET 10.0 SDK installed)
./build.sh Release --test
```

The native build uses the same `EnableWindowsTargeting` mechanism and produces identical artifacts.

## Contributing

When adding new projects or dependencies:
1. Update `Directory.Build.props` if needed
2. Ensure projects build in both Debug and Release configurations
3. Add tests to `Loquacio.Tests` or `Loquacio.Wpf.Tests`
4. Verify the Docker build still works: `./docker-build.sh --test`

## Further Reading

- [.NET SDK Docker Images](https://hub.docker.com/_/microsoft-dotnet-sdk)
- [EnableWindowsTargeting](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props)
- [Docker Multi-Stage Builds](https://docs.docker.com/build/building/multi-stage/)
- [Docker Best Practices](https://docs.docker.com/develop/dev-best-practices/)

## Support

For issues or questions about the Docker workflow:
1. Check the troubleshooting section above
2. Review the Dockerfile and docker-build.sh script
3. Open an issue on the project repository
