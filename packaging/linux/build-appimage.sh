#!/bin/bash
set -euo pipefail

# Build AppImage for whisper-dictation (Phase D task 2 continuation).
# Usage:
#   packaging/linux/build-appimage.sh [--skip-build]
#
# This script builds on the existing portable tarball from build-packages.sh.
# It creates an AppImage that can run on any Linux distribution with
# glibc >= 2.17 (most modern distros).

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

SKIP_BUILD=false
for arg in "$@"; do
  case $arg in
    --skip-build) SKIP_BUILD=true ;;
  esac
done

# Build the base packages first (creates dist/publish/linux-x64)
if [[ "$SKIP_BUILD" != true ]]; then
  echo "Building base packages..."
  packaging/linux/build-packages.sh --build-only
fi

DIST="$REPO_ROOT/dist"
PUBLISH="$DIST/publish/linux-x64"
APP=whisper-dictation
APPDIR="$DIST/AppDir"

# Version info
BASE="$(git describe --tags --abbrev=0 2>/dev/null || echo 0.0.0)"
VERSION="${BASE#v}.git$(git rev-parse --short HEAD)"

echo "Building AppImage version $VERSION..."

# ---- Create AppDir structure ----
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/share/applications"
mkdir -p "$APPDIR/usr/share/icons/hicolor/256x256/apps"
mkdir -p "$APPDIR/usr/share/doc/$APP"
mkdir -p "$APPDIR/usr/lib/$APP"

# Copy published binaries
cp -r "$PUBLISH"/. "$APPDIR/usr/lib/$APP/"
chmod 755 "$APPDIR/usr/lib/$APP/whisper-dictation-daemon"
chmod 755 "$APPDIR/usr/lib/$APP/whisper-dictation-tui"
chmod 755 "$APPDIR/usr/lib/$APP/whisper-dictation-avalonia" 2>/dev/null || true

# Create wrapper scripts in /usr/bin
cat > "$APPDIR/usr/bin/whisper-dictation-daemon" <<'EOS'
#!/bin/sh
cd "$(dirname "$0")/../lib/whisper-dictation"
exec ./whisper-dictation-daemon "$@"
EOS
cat > "$APPDIR/usr/bin/whisper-dictation-tui" <<'EOS'
#!/bin/sh
cd "$(dirname "$0")/../lib/whisper-dictation"
exec ./whisper-dictation-tui "$@"
EOS
cat > "$APPDIR/usr/bin/whisper-dictation-avalonia" <<'EOS'
#!/bin/sh
cd "$(dirname "$0")/../lib/whisper-dictation"
exec ./whisper-dictation-avalonia "$@"
EOS
chmod 755 "$APPDIR/usr/bin/whisper-dictation-"*

# ---- Copy documentation ----
cp README.md LICENSE "$APPDIR/usr/share/doc/$APP/" 2>/dev/null || true

# ---- Create .desktop file ----
cat > "$APPDIR/usr/share/applications/$APP.desktop" <<EOF
[Desktop Entry]
Name=Whisper Dictation
Comment=Local Whisper-powered voice-to-text dictation
Exec=whisper-dictation-avalonia
Icon=whisper-dictation
Type=Application
Categories=Utility;
Terminal=false
StartupNotify=true
Keywords=voice;dictation;speech;transcription;whisper;
EOF

# ---- Create icon (simple SVG) ----
cat > "$APPDIR/usr/share/icons/hicolor/256x256/apps/whisper-dictation.svg" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256">
  <defs>
    <linearGradient id="grad1" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" style="stop-color:#4CAF50;stop-opacity:1" />
      <stop offset="100%" style="stop-color:#2E7D32;stop-opacity:1" />
    </linearGradient>
  </defs>
  <rect width="256" height="256" rx="48" fill="url(#grad1)"/>
  <path d="M64 128 Q96 64 128 128 T192 128" stroke="white" stroke-width="16" fill="none" stroke-linecap="round"/>
  <circle cx="128" cy="128" r="24" fill="white"/>
  <circle cx="64" cy="128" r="16" fill="white" opacity="0.7"/>
  <circle cx="192" cy="128" r="16" fill="white" opacity="0.7"/>
</svg>
EOF
# appimagetool expects the .desktop and icon at the AppDir root
cp "$APPDIR/usr/share/applications/$APP.desktop" "$APPDIR/"
cp "$APPDIR/usr/share/icons/hicolor/256x256/apps/whisper-dictation.svg" "$APPDIR/"

# ---- Create AppRun script ----
cat > "$APPDIR/AppRun" <<'EOS'
#!/bin/bash
cd "$(dirname "$0")"

# Export library path for bundled dependencies
export LD_LIBRARY_PATH="${APPDIR}/usr/lib/whisper-dictation:${LD_LIBRARY_PATH:-}"

# Add /usr/bin to PATH
export PATH="${APPDIR}/usr/bin:${PATH:-}"

# Launch the requested controller; default is the GUI (avalonia)
case "${1:-}" in
  daemon|whisper-dictation-daemon) shift; exec whisper-dictation-daemon "$@" ;;
  tui|whisper-dictation-tui) shift; exec whisper-dictation-tui "$@" ;;
esac
exec whisper-dictation-avalonia "$@"
EOS
chmod 755 "$APPDIR/AppRun"

# ---- Download appimagetool if not present ----
APPIMAGETOOL="$DIST/appimagetool-x86_64.AppImage"
if [[ ! -f "$APPIMAGETOOL" ]]; then
  echo "Downloading appimagetool..."
  curl -L -o "$APPIMAGETOOL" "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
  chmod +x "$APPIMAGETOOL"
fi

# FUSE-less hosts: extract the tool once and run its AppRun directly
if ! ldconfig -p 2>/dev/null | grep -q libfuse.so.2; then
  if [[ ! -d "$DIST/appimagetool-squashfs" ]]; then
    echo "libfuse2 not found; extracting appimagetool (no-FUSE mode)..."
    "$APPIMAGETOOL" --appimage-extract >/dev/null
    mv squashfs-root "$DIST/appimagetool-squashfs"
  fi
  APPIMAGETOOL="$DIST/appimagetool-squashfs/AppRun"
fi

# ---- Build AppImage ----
OUTPUT="$DIST/${APP}-${VERSION}-x86_64.AppImage"
echo "Building AppImage: $OUTPUT"

ARCH=x86_64 "$APPIMAGETOOL" --no-appstream "$APPDIR" "$OUTPUT"

echo "✅ AppImage built: $OUTPUT"
echo ""
echo "To run:"
echo "  ./$OUTPUT"
echo ""
echo "To install system-wide:"
echo "  sudo cp $OUTPUT /opt/"
echo "  sudo ln -s /opt/$(basename $OUTPUT) /usr/local/bin/whisper-dictation"
