#!/bin/bash
set -euo pipefail

# Build portable Windows ZIP for whisper-dictation (Phase D: win portable packaging).
# Runs on Linux via dotnet cross-publish (EnableWindowsTargeting, see Directory.Build.props).
# Usage:
#   packaging/windows/build-portable.sh            # publish + package
#   packaging/windows/build-portable.sh --skip-publish
#
# NOTE: the artifact is verified structurally (PE32+ magic, zip contents) but cannot
# be executed on Linux. MSIX remains a Windows-host-only follow-up (makeappx is
# Windows-only); see build-portable.ps1 / build-msix.ps1 for Windows-host builds.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

SKIP_PUBLISH=false
for arg in "$@"; do
  case $arg in
    --skip-publish) SKIP_PUBLISH=true ;;
  esac
done

RID=win-x64
CONFIGURATION=Release
DIST="$REPO_ROOT/dist"
PUBLISH="$DIST/publish/$RID"
APP=whisper-dictation

# Version info (mirrors build-appimage.sh conventions)
BASE="$(git describe --tags --abbrev=0 2>/dev/null || echo 0.0.0)"
VERSION="${BASE#v}.git$(git rev-parse --short HEAD)"
NUGET_VERSION="${BASE#v}+$(git rev-parse --short HEAD)"

echo "Building Windows portable package version $VERSION..."

PROJECTS=(
  WhisperDictation.Daemon/WhisperDictation.Daemon.csproj
  WhisperDictation.Tui/WhisperDictation.Tui.csproj
  WhisperDictation.Avalonia/WhisperDictation.Avalonia.csproj
)

if [[ "$SKIP_PUBLISH" != true ]]; then
  mkdir -p "$PUBLISH"
  for proj in "${PROJECTS[@]}"; do
    echo "Publishing $proj ($RID, self-contained)..."
    dotnet publish "$proj" \
      -c "$CONFIGURATION" \
      -r "$RID" \
      --self-contained true \
      -p:PublishSingleFile=false \
      -p:Version="$NUGET_VERSION" \
      -o "$PUBLISH"
  done
  echo "Publish complete: $PUBLISH"
fi

[[ -f "$PUBLISH/whisper-dictation-daemon.exe" ]] || { echo "ERROR: daemon exe missing from publish output" >&2; exit 1; }

# ---- Stage portable layout ----
STAGE="$DIST/stage-win/$APP"
rm -rf "$DIST/stage-win"; mkdir -p "$STAGE"
cp -r "$PUBLISH"/. "$STAGE/"
cp README.md LICENSE "$STAGE/" 2>/dev/null || true

COMMIT="$(git rev-parse --short HEAD)"
cat > "$STAGE/README.txt" <<EOF
Whisper Dictation - Portable Windows Package ($VERSION)
=====================================================

Contents
--------
  whisper-dictation-daemon.exe     Background daemon (audio capture + transcription)
  whisper-dictation-tui.exe        Terminal UI controller
  whisper-dictation-avalonia.exe   GUI controller
  (*.dll, *.json are self-contained .NET 10 runtime files - keep them next to the exes)

Quick Start
-----------
1. Extract this ZIP to any folder.
2. Run whisper-dictation-daemon.exe to start the daemon, then a controller
   (whisper-dictation-tui.exe or whisper-dictation-avalonia.exe).

Run at Login (Task Scheduler hint)
----------------------------------
Open an elevated Command Prompt in this folder and run:

  schtasks /create /tn "WhisperDictation Daemon" /tr "%CD%\\whisper-dictation-daemon.exe" /sc onlogon /rl highest /f

Remove it later with:

  schtasks /delete /tn "WhisperDictation Daemon" /f

Notes
-----
- Settings: %APPDATA%\\WhisperDictation\\settings.json
- Models:   %LOCALAPPDATA%\\WhisperDictation\\models
- No installation required; no .NET runtime needed (self-contained).
- Built from commit: $COMMIT
EOF

# ---- Zip artifact ----
ZIP="$DIST/${APP}-${VERSION}-${RID}-portable.zip"
rm -f "$ZIP"
( cd "$DIST/stage-win" && zip -qr "$ZIP" "$APP" )
echo "portable zip: dist/${APP}-${VERSION}-${RID}-portable.zip"
