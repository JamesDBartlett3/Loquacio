#!/bin/bash
set -euo pipefail

# Build self-contained Linux packages for loquacio (Phase D task 2).
# Usage:
#   packaging/linux/build-packages.sh                # docker publish + host packaging
#   packaging/linux/build-packages.sh --build-only   # publish only (already inside docker / native dotnet)
#   packaging/linux/build-packages.sh --skip-publish # package from existing dist/publish
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

BASE="$(git describe --tags --abbrev=0 2>/dev/null || echo 0.0.0)"
NUGET_VERSION="${BASE#v}+$(git rev-parse --short HEAD)"
VERSION="${BASE#v}.git$(git rev-parse --short HEAD)"
RID=linux-x64
CONFIGURATION=Release
DIST="$REPO_ROOT/dist"
PUBLISH="$DIST/publish/$RID"
APP=loquacio

BUILD_ONLY=false
SKIP_PUBLISH=false
for arg in "$@"; do
  case $arg in
    --build-only) BUILD_ONLY=true ;;
    --skip-publish) SKIP_PUBLISH=true ;;
  esac
done

publish() {
  local project="$1"
  dotnet publish "$project" -c "$CONFIGURATION" -r "$RID" \
    --self-contained true -p:PublishSingleFile=false \
    -p:Version="$NUGET_VERSION" -o "$PUBLISH"
}

if [[ "$SKIP_PUBLISH" != true ]]; then
  if command -v dotnet >/dev/null 2>&1; then
    publish Loquacio.Daemon/Loquacio.Daemon.csproj
    publish Loquacio.Tui/Loquacio.Tui.csproj
    publish Loquacio.Avalonia/Loquacio.Avalonia.csproj
  else
    echo "No local dotnet — running publish inside docker"
    docker run --rm \
      -v "$REPO_ROOT:/app" -w /app \
      -e XDG_RUNTIME_DIR=/tmp/runtime \
      --user "$(id -u):$(id -g)" \
      mcr.microsoft.com/dotnet/sdk:10.0 \
      packaging/linux/build-packages.sh --build-only
  fi
fi

[[ "$BUILD_ONLY" == true ]] && { echo "Publish complete: $PUBLISH"; exit 0; }

command -v dpkg-deb >/dev/null || { echo "dpkg-deb required for .deb packaging" >&2; exit 1; }

# ---- Portable tarball ----
STAGE="$DIST/stage-tar/$APP"
rm -rf "$STAGE"; mkdir -p "$STAGE"
cp -r "$PUBLISH"/. "$STAGE/"
cp README.md LICENSE "$STAGE/" 2>/dev/null || true
tar -C "$DIST/stage-tar" -czf "$DIST/${APP}-${VERSION}-${RID}.tar.gz" "$APP"
echo "✅ tarball: dist/${APP}-${VERSION}-${RID}.tar.gz"

# ---- Debian package ----
PKGROOT="$DIST/stage-deb/${APP}_${VERSION}_amd64"
rm -rf "$DIST/stage-deb"; mkdir -p "$PKGROOT"
LIBDIR="$PKGROOT/usr/lib/$APP"
BINDIR="$PKGROOT/usr/bin"
mkdir -p "$LIBDIR" "$BINDIR" "$PKGROOT/DEBIAN" \
  "$PKGROOT/usr/lib/systemd/user"

cp -r "$PUBLISH"/. "$LIBDIR/"
chmod 755 "$LIBDIR/loquacio-daemon" \
          "$LIBDIR/loquacio-tui" \
          "$LIBDIR/loquacio-avalonia" 2>/dev/null || true
install -m 644 "$SCRIPT_DIR/loquacio-daemon.service" \
  "$PKGROOT/usr/lib/systemd/user/loquacio-daemon.service"

# Thin launch wrappers so /usr/bin entries are just the per-app host binaries.
cat > "$BINDIR/loquacio-daemon" <<'EOS'
#!/bin/sh
exec /usr/lib/loquacio/loquacio-daemon "$@"
EOS
cat > "$BINDIR/loquacio-tui" <<'EOS'
#!/bin/sh
exec /usr/lib/loquacio/loquacio-tui "$@"
EOS
cat > "$BINDIR/loquacio-avalonia" <<'EOS'
#!/bin/sh
exec /usr/lib/loquacio/loquacio-avalonia "$@"
EOS
chmod 755 "$BINDIR"/loquacio-*

cat > "$PKGROOT/DEBIAN/control" <<EOF
Package: loquacio
Version: ${VERSION}
Section: sound
Priority: optional
Architecture: amd64
Depends: libasound2 (>= 1.0.27), pipewire (>= 0.3) | pulseaudio
Maintainer: James D. Bartlett III <james@datavolume.xyz>
Description: Local Whisper-powered voice-to-text dictation (daemon + TUI + GUI)
 Self-contained local dictation stack: audio capture (PipeWire), Whisper
 transcription, optional local-LLM post-processing. Includes the daemon,
 a TUI controller, an Avalonia GUI controller, and a systemd user unit.
 Installed to /usr/lib/loquacio; no .NET runtime required.
Homepage: https://elitedesk.local:3001/Innovation/loquacio
EOF
cat > "$PKGROOT/DEBIAN/postinst" <<'EOS'
#!/bin/sh
set -e
systemctl --user daemon-reload >/dev/null 2>&1 || true
echo "loquacio installed. Enable the daemon with:"
echo "  systemctl --user enable --now loquacio-daemon.service"
EOS
chmod 755 "$PKGROOT/DEBIAN/postinst"

mkdir -p "$DIST"
dpkg-deb --root-owner-group -Zxz \
  --build "$PKGROOT" "$DIST/loquacio_${VERSION}_amd64.deb"
echo "✅ deb: dist/loquacio_${VERSION}_amd64.deb"
