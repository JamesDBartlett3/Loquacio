#!/bin/bash
# Build macOS .app bundle for whisper-dictation (Phase D task 2).
# This script MUST be run on macOS (not Linux).
#
# Usage:
#   packaging/macos/build-app.sh [--skip-build]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

# Check we're on macOS
if [[ "$(uname)" != "Darwin" ]]; then
  echo "ERROR: This script must be run on macOS" >&2
  exit 1
fi

SKIP_BUILD=false
for arg in "$@"; do
  case $arg in
    --skip-build) SKIP_BUILD=true ;;
  esac
done

DIST="$REPO_ROOT/dist"
PUBLISH="$DIST/publish/osx-x64"
APP_NAME="WhisperDictation"
APP_BUNDLE="$DIST/${APP_NAME}.app"
CONTENTS="$APP_BUNDLE/Contents"

# Version info
BASE="$(git describe --tags --abbrev=0 2>/dev/null || echo 0.0.0)"
VERSION="${BASE#v}.git$(git rev-parse --short HEAD)"
NUGET_VERSION="${BASE#v}+$(git rev-parse --short HEAD)"

echo "Building macOS .app bundle version $VERSION..."

# ---- Build the binaries ----
if [[ "$SKIP_BUILD" != true ]]; then
  echo "Publishing binaries for osx-x64..."
  dotnet publish WhisperDictation.Daemon/WhisperDictation.Daemon.csproj -c Release -r osx-x64 \
    --self-contained true -p:PublishSingleFile=false -p:Version="$NUGET_VERSION" -o "$PUBLISH"
  dotnet publish WhisperDictation.Avalonia/WhisperDictation.Avalonia.csproj -c Release -r osx-x64 \
    --self-contained true -p:PublishSingleFile=false -p:Version="$NUGET_VERSION" -o "$PUBLISH"
  dotnet publish WhisperDictation.Tui/WhisperDictation.Tui.csproj -c Release -r osx-x64 \
    --self-contained true -p:PublishSingleFile=false -p:Version="$NUGET_VERSION" -o "$PUBLISH"
fi

# ---- Create .app bundle structure ----
rm -rf "$APP_BUNDLE"
mkdir -p "$CONTENTS/MacOS"
mkdir -p "$CONTENTS/Resources"
mkdir -p "$CONTENTS/Frameworks"

# ---- Copy binaries to Frameworks ----
cp -r "$PUBLISH"/. "$CONTENTS/Frameworks/"
chmod 755 "$CONTENTS/Frameworks/whisper-dictation-daemon"
chmod 755 "$CONTENTS/Frameworks/whisper-dictation-tui"
chmod 755 "$CONTENTS/Frameworks/whisper-dictation-avalonia" 2>/dev/null || true

# ---- Create launcher script ----
cat > "$CONTENTS/MacOS/whisper-dictation" <<'EOS'
#!/bin/bash
cd "$(dirname "$0")/../Frameworks"
exec ./whisper-dictation-avalonia "$@"
EOS
chmod 755 "$CONTENTS/MacOS/whisper-dictation"

# ---- Create Info.plist ----
cat > "$CONTENTS/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDisplayName</key>
    <string>Whisper Dictation</string>
    <key>CFBundleExecutable</key>
    <string>whisper-dictation</string>
    <key>CFBundleIconFile</key>
    <string>app-icon</string>
    <key>CFBundleIdentifier</key>
    <string>com.github.jamesdbartlett3.whisper-dictation</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>Whisper Dictation</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSMicrophoneUsageDescription</key>
    <string>Whisper Dictation needs microphone access to capture voice for transcription.</string>
    <key>NSAppleEventsUsageDescription</key>
    <string>Whisper Dictation needs Apple Events permission to inject text into other applications.</string>
    <key>LSUIElement</key>
    <false/>
</dict>
</plist>
EOF

# ---- Create icon (simple .icns) ----
# Note: In production, use a proper .icns file created from high-res PNGs
# This is a minimal placeholder that works
ICON_SVG="$CONTENTS/Resources/app-icon.svg"
cat > "$ICON_SVG" <<'EOS'
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
EOS

# Convert SVG to ICNS (requires sips or iconutil)
if command -v iconutil >/dev/null 2>&1; then
  # Create iconset
  ICONSET="$CONTENTS/Resources/app-icon.iconset"
  mkdir -p "$ICONSET"
  # Generate PNGs at required sizes (this is simplified - real build would use proper source art)
  sips -z 16 16 "$ICON_SVG" --out "$ICONSET/icon_16x16.png" >/dev/null 2>&1 || true
  sips -z 32 32 "$ICON_SVG" --out "$ICONSET/icon_16x16@2x.png" >/dev/null 2>&1 || true
  sips -z 32 32 "$ICON_SVG" --out "$ICONSET/icon_32x32.png" >/dev/null 2>&1 || true
  sips -z 64 64 "$ICON_SVG" --out "$ICONSET/icon_32x32@2x.png" >/dev/null 2>&1 || true
  sips -z 128 128 "$ICON_SVG" --out "$ICONSET/icon_128x128.png" >/dev/null 2>&1 || true
  sips -z 256 256 "$ICON_SVG" --out "$ICONSET/icon_128x128@2x.png" >/dev/null 2>&1 || true
  sips -z 256 256 "$ICON_SVG" --out "$ICONSET/icon_256x256.png" >/dev/null 2>&1 || true
  sips -z 512 512 "$ICON_SVG" --out "$ICONSET/icon_256x256@2x.png" >/dev/null 2>&1 || true
  sips -z 512 512 "$ICON_SVG" --out "$ICONSET/icon_512x512.png" >/dev/null 2>&1 || true
  sips -z 1024 1024 "$ICON_SVG" --out "$ICONSET/icon_512x512@2x.png" >/dev/null 2>&1 || true
  iconutil -c icns "$ICONSET" -o "$CONTENTS/Resources/app-icon.icns"
  rm -rf "$ICONSET"
else
  echo "WARNING: iconutil not found, skipping .icns generation"
  echo "The app will use a default icon"
fi

# ---- Create launchd agent template ----
cat > "$CONTENTS/Resources/com.github.jamesdbartlett3.whisper-dictation.daemon.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.github.jamesdbartlett3.whisper-dictation.daemon</string>
    <key>ProgramArguments</key>
    <array>
        <string>$CONTENTS/Frameworks/whisper-dictation-daemon</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>~/Library/Logs/WhisperDictation/daemon.log</string>
    <key>StandardErrorPath</key>
    <string>~/Library/Logs/WhisperDictation/daemon-error.log</string>
    <key>ProcessType</key>
    <string>Interactive</string>
</dict>
</plist>
EOF

# ---- Code sign with ad-hoc signature ----
if command -v codesign >/dev/null 2>&1; then
  echo "Code signing with ad-hoc signature..."
  codesign --force --deep --sign - "$APP_BUNDLE" 2>/dev/null || echo "WARNING: Code signing failed"
else
  echo "WARNING: codesign not found, app may not launch"
fi

echo "✅ .app bundle built: $APP_BUNDLE"
echo ""
echo "To test:"
echo "  open \"$APP_BUNDLE\""
echo ""
echo "To install:"
echo "  cp -R \"$APP_BUNDLE\" /Applications/"
