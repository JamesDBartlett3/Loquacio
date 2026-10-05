#!/bin/bash
set -uo pipefail

# Docker Build & Test Script for Loquacio
# ================================================
# This script orchestrates building and testing the Loquacio solution
# inside a Docker container using the official .NET SDK image.
#
# This enables building Windows-targeting code (WPF app) from Linux thanks to
# EnableWindowsTargeting in Directory.Build.props.
#
# Usage:
#   ./docker-build.sh                    # Build (Release) + no tests
#   ./docker-build.sh --test            # Build (Release) + run tests
#   ./docker-build.sh Debug --test      # Build (Debug) + run tests
#   ./docker-build.sh --clean           # Clean build artifacts first
#   ./docker-build.sh --no-cache        # Rebuild Docker image without cache
#   ./docker-build.sh --shell           # Start shell in container for debugging

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

# Parse arguments
CONFIGURATION="Release"
RUN_TESTS=false
CLEAN_BUILD=false
NO_CACHE=false
START_SHELL=false
DOCKER_IMAGE_NAME="loquacio-build"
DOCKER_CONTAINER_NAME="loquacio-build-$$"

while [[ $# -gt 0 ]]; do
    case $1 in
        Debug|Release)
            CONFIGURATION="$1"
            shift
            ;;
        --test|-t)
            RUN_TESTS=true
            shift
            ;;
        --clean)
            CLEAN_BUILD=true
            shift
            ;;
        --no-cache)
            NO_CACHE=true
            shift
            ;;
        --shell)
            START_SHELL=true
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [Debug|Release] [--test] [--clean] [--no-cache] [--shell]"
            echo ""
            echo "Options:"
            echo "  Debug|Release    Build configuration (default: Release)"
            echo "  --test, -t       Run tests after build"
            echo "  --clean          Clean build artifacts before building"
            echo "  --no-cache       Rebuild Docker image without cache"
            echo "  --shell          Start interactive shell in container"
            echo "  --help, -h       Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

echo "=== Loquacio Docker Build ==="
echo "Configuration: $CONFIGURATION"
echo "Run tests: $RUN_TESTS"
echo "Clean build: $CLEAN_BUILD"
echo "No cache: $NO_CACHE"
echo "Shell mode: $START_SHELL"
echo ""

# Clean build artifacts if requested
if [ "$CLEAN_BUILD" = true ]; then
    echo "Cleaning local build artifacts..."
    rm -rf */bin */obj .dotnet
fi

# Build Docker image
echo "Building Docker image..."
BUILD_ARGS=""
if [ "$NO_CACHE" = true ]; then
    BUILD_ARGS="--no-cache"
fi

docker build $BUILD_ARGS \
    --build-arg CONFIGURATION="$CONFIGURATION" \
    -t "$DOCKER_IMAGE_NAME" \
    -f Dockerfile . || {
    echo "❌ Docker image build failed"
    exit 1
}

echo "✅ Docker image built successfully"
echo ""

# Start shell in container if requested
if [ "$START_SHELL" = true ]; then
    echo "Starting interactive shell in container..."
    docker run --rm -it \
        -v "$(pwd):/app" \
        -w /app \
        -e XDG_RUNTIME_DIR=/tmp/runtime \
        -e HOME=/tmp \
        -e NUGET_PACKAGES=/tmp/nuget \
        --user "$(id -u):$(id -g)" \
        "$DOCKER_IMAGE_NAME" \
        /bin/bash
    exit $?
fi

# Run build and tests in container
echo "Running build in container..."
BUILD_ARGS=($CONFIGURATION)
if [ "$RUN_TESTS" = true ]; then
    BUILD_ARGS+=("--test")
fi

docker run --rm \
    -v "$(pwd):/app" \
    -w /app \
    -e XDG_RUNTIME_DIR=/tmp/runtime \
    -e HOME=/tmp \
    -e NUGET_PACKAGES=/tmp/nuget \
    --user "$(id -u):$(id -g)" \
    "$DOCKER_IMAGE_NAME" \
    ./build.sh "${BUILD_ARGS[@]}" || {
    BUILD_EXIT_CODE=$?
    echo ""
    echo "❌ Docker build failed with exit code: $BUILD_EXIT_CODE"
    exit $BUILD_EXIT_CODE
}

BUILD_EXIT_CODE=0

if [ $BUILD_EXIT_CODE -eq 0 ]; then
    echo ""
    echo "✅ Docker build completed successfully"
    echo ""
    echo "Build artifacts are available in:"
    echo "  - Loquacio/bin/$CONFIGURATION/net10.0-windows/"
    echo "  - Loquacio.Core/bin/$CONFIGURATION/net10.0/"
    echo "  - TestConsole/bin/$CONFIGURATION/net10.0-windows/"
    echo "  - Loquacio.Tests/bin/$CONFIGURATION/net10.0/"
else
    echo ""
    echo "❌ Docker build failed with exit code: $BUILD_EXIT_CODE"
fi

exit $BUILD_EXIT_CODE
