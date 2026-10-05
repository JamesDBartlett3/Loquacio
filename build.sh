#!/bin/bash
set -euo pipefail

# Build script for Whisper Dictation
# Works natively on Linux thanks to EnableWindowsTargeting (see Directory.Build.props)

CONFIGURATION="${1:-Release}"
RUN_TESTS=false

if [[ "${2:-}" == "--test" || "${2:-}" == "-t" ]]; then
    RUN_TESTS=true
fi

cd "$(dirname "$0")"

echo "=== Whisper Dictation Build ==="
echo "Configuration: $CONFIGURATION"
echo "Tests: $RUN_TESTS"
echo ""

# Clean previous artifacts if requested
if [[ "${2:-}" == "--clean" || "${3:-}" == "--clean" ]]; then
    echo "Cleaning previous build artifacts..."
    rm -rf */bin */obj
fi

# Build entire solution
echo "Building solution..."
dotnet build WhisperDictation.slnx -c "$CONFIGURATION"
echo "✅ Build succeeded"

# Run tests
if [ "$RUN_TESTS" = true ]; then
    echo ""
    echo "Running tests..."
    dotnet test WhisperDictation.Tests/WhisperDictation.Tests.csproj \
        -c "$CONFIGURATION" --no-build --verbosity normal
    echo "✅ All tests passed"
fi

echo ""
echo "Build summary:"
echo "  - WhisperDictation.Core (net10.0) — cross-platform library"
echo "  - WhisperDictation (net10.0-windows) — WPF application"
echo "  - WhisperDictation.Tests (net10.0) — cross-platform tests"
echo "  - TestConsole (net10.0-windows) — Windows test harness"
