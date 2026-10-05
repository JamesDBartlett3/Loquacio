# Cross-Platform Packaging (Phase D)

This directory contains packaging scripts and specifications for Whisper Dictation across all supported platforms.

## Platform Coverage

| Platform | Formats | Status | Location |
|---|---|---|---|
| **Linux** | .deb, .tar.gz, .AppImage, Flatpak | ✅ Complete | `linux/` |
| **Windows** | MSIX, Portable .zip | ✅ Portable validated 2026-10-02 (cross-built on Linux); MSIX needs a Windows host | `windows/` |
| **macOS** | .app bundle, Homebrew cask | 🔶 Spec only | `macos/` |

## Linux Packaging (`linux/`)

### Quick Start

```bash
# Build all Linux packages
./packaging/linux/build-packages.sh

# Build only AppImage (requires base packages first)
./packaging/linux/build-appimage.sh

# Build Flatpak (requires flatpak installed)
flatpak-builder --repo=flatpak-repo flatpak-build packaging/linux/com.github.jamesdbartlett3.whisper-dictation.json
```

### Package Formats

#### 1. Debian Package (.deb)

**Script:** `packaging/linux/build-packages.sh`

**Output:** `dist/whisper-dictation_<version>_amd64.deb`

**Installation:**
```bash
sudo dpkg -i dist/whisper-dictation_*_amd64.deb
systemctl --user enable --now whisper-dictation-daemon.service
```

**Features:**
- Installs to `/usr/lib/whisper-dictation/`
- Wrapper scripts in `/usr/bin/`
- Systemd user service (`~/.config/systemd/user/`)
- Dependencies: `libasound2`, `pipewire` or `pulseaudio`

#### 2. Portable Tarball (.tar.gz)

**Script:** `packaging/linux/build-packages.sh`

**Output:** `dist/whisper-dictation-<version>-linux-x64.tar.gz`

**Usage:**
```bash
tar -xzf whisper-dictation-<version>-linux-x64.tar.gz
cd whisper-dictation
./whisper-dictation-daemon
./whisper-dictation-tui
./whisper-dictation-avalonia
```

**Features:**
- Self-contained (no .NET runtime required)
- Can be run from any location
- AppImage-ready structure

#### 3. AppImage

**Script:** `packaging/linux/build-appimage.sh`

**Output:** `dist/whisper-dictation-<version>-x86_64.AppImage`

**Usage:**
```bash
chmod +x whisper-dictation-<version>-x86_64.AppImage
./whisper-dictation-<version>-x86_64.AppImage
```

**Features:**
- Single-file executable
- Runs on any Linux distribution (glibc >= 2.17)
- Includes GUI launcher
- No installation required

#### 4. Flatpak

**Manifest:** `packaging/linux/com.github.jamesdbartlett3.whisper-dictation.json`

**Build:**
```bash
flatpak install flathub org.freedesktop.Platform//24.08 org.freedesktop.Sdk//24.08 org.freedesktop.Platform.Dotnet//8.0
flatpak-builder --repo=flatpak-repo flatpak-build packaging/linux/com.github.jamesdbartlett3.whisper-dictation.json
flatpak build-bundle flatpak-repo whisper-dictation.flatpak com.github.jamesdbartlett3.whisper-dictation
```

**Installation:**
```bash
flatpak install whisper-dictation.flatpak
flatpak run com.github.jamesdbartlett3.whisper-dictation
```

**Features:**
- Sandboxed environment
- Works across all Linux distributions
- Centralized updates via Flatpak

## Windows Validation (2026-10-02, elitedesk)

`build-portable.ps1` was executed on Linux (PowerShell 7.6.5, dotnet SDK 10.0.112) as a cross-publish validation:

- `dotnet publish -r win-x64 --self-contained` succeeded for Daemon, WPF app, and TUI (0 errors)
- Artifact: `dist/whisper-dictation-0.0.0.gitf17e126-win-x64-portable.zip` (86,067,634 bytes, 571 files)
- Contains whisper-dictation-daemon.exe, whisper-dictation-tui.exe, WhisperDictation.exe (all PE32+ / MZ magic verified), plus Start/Install/Uninstall .bat helpers
- NOT executed (no Windows on this host): runtime behavior, Task Scheduler scripts, and MSIX packaging (build-msix.ps1 requires makeappx on Windows). Runtime verification on a Windows 11 host is the remaining follow-up (see Kanboard #38).

## Windows Packaging (`windows/`)

### Quick Start

```powershell
# Build MSIX package (for Microsoft Store)
.\packaging\windows\build-msix.ps1

# Build portable ZIP
.\packaging\windows\build-portable.ps1
```

```bash
# Build portable ZIP on Linux via dotnet cross-publish (EnableWindowsTargeting)
./packaging/windows/build-portable.sh
```

### Package Formats

#### 1. MSIX Package

**Script:** `packaging/windows/build-msix.ps1`

**Output:** `dist/WhisperDictation.Package_<version>_x64.msix`

**Installation:**
```powershell
Add-AppxPackage .\dist\WhisperDictation.Package_*.msix
```

**Features:**
- Microsoft Store compatible
- Auto-update support
- Clean installation/uninstallation

#### 2. Portable ZIP

**Script:** `packaging/windows/build-portable.ps1`

**Output:** `dist/whisper-dictation-<version>-win-x64-portable.zip`

**Usage:**
```powershell
# Extract and run
Expand-Archive .\whisper-dictation-*-win-x64-portable.zip -DestinationPath .
.\Start\ Whisper\ Dictation.bat

# Install daemon as scheduled task
.\Install\ Daemon.bat
```

**Features:**
- No installation required
- Includes Task Scheduler setup scripts
- All three controllers (WPF, TUI, Daemon)

## macOS Packaging (`macOS/`)

**Note:** macOS packages can only be built on macOS. This directory contains specifications and build scripts that will work when run on macOS.

### Quick Start (on macOS)

```bash
# Build .app bundle
./packaging/macos/build-app.sh

# This will create:
# - dist/WhisperDictation.app
# - Launchd agent template
# - Code-signed application
```

### Package Format

#### .app Bundle

**Script:** `packaging/macos/build-app.sh` (macOS only)

**Output:** `dist/WhisperDictation.app`

**Installation:**
```bash
cp -R dist/WhisperDictation.app /Applications/
open /Applications/WhisperDictation.app
```

**Features:**
- Standard macOS application bundle
- CGEvent text injection
- Launchd agent support
- Requires accessibility and microphone permissions

### Homebrew Cask

**Formula:** `packaging/macos/whisper-dictation.rb`

**Installation:**
```bash
brew install --cask whisper-dictation
```

**CI/CD:**

GitHub Actions workflow: `.github/workflows/build-macos.yml`

Builds .app bundle on macOS runners and uploads as artifact.

## Auto-Update Mechanism

### Implementation

The auto-update service is implemented in:
- `WhisperDictation.Core/Services/IUpdateService.cs` - Interface
- `WhisperDictation.Daemon/Services/GitHubUpdateService.cs` - GitHub-based implementation
- Registered in `WhisperDictation.Daemon/DaemonServiceRegistration.cs`

### How It Works

1. **Check for Updates:** Daemon checks GitHub releases API on startup
2. **Download:** If update available, downloads package to temp directory
3. **Install:** Platform-specific installation:
   - **Linux:** Shows instructions to user (no silent install due to security)
   - **Windows:** Launches installer with elevation
   - **macOS:** Shows instructions to user

### Usage

The update service is automatically registered in the daemon. To check for updates programmatically:

```csharp
var updateService = serviceProvider.GetRequiredService<IUpdateService>();
var updateInfo = await updateService.CheckForUpdatesAsync();

if (updateInfo != null)
{
    Console.WriteLine($"Update available: {updateInfo.Version}");
    Console.WriteLine($"Release notes: {updateInfo.ReleaseNotes}");

    var filePath = await updateService.DownloadUpdateAsync(updateInfo, progress);
    await updateService.InstallUpdateAsync(filePath);
}
```

## Versioning

All packages use version information derived from git tags:

```bash
# Tag a release
git tag v1.0.0
git push origin v1.0.0

# Version format: v1.0.0.git+a1b2c3d
# NuGet version: 1.0.0+a1b2c3d
```

## Building All Packages

To build packages for all platforms:

```bash
# Linux
./packaging/linux/build-packages.sh
./packaging/linux/build-appimage.sh

# Windows (requires PowerShell)
pwsh -File packaging/windows/build-portable.ps1

# macOS (requires macOS)
./packaging/macos/build-app.sh
```

## Testing

After building packages, verify:

### Linux
```bash
# Test .deb
sudo dpkg -i dist/whisper-dictation_*.deb
systemctl --user start whisper-dictation-daemon.service
/usr/bin/whisper-dictation-tui

# Test AppImage
chmod +x dist/whisper-dictation-*.AppImage
./dist/whisper-dictation-*.AppImage

# Test Flatpak
flatpak install whisper-dictation.flatpak
flatpak run com.github.jamesdbartlett3.whisper-dictation
```

### Windows
```powershell
# Test portable
Expand-Archive .\dist\whisper-dictation-*-win-x64-portable.zip -DestinationPath .\test
.\test\Start\ Whisper\ Dictation.bat
```

### macOS (on macOS)
```bash
# Test .app
open dist/WhisperDictation.app
```

## Distribution

### GitHub Releases

All packages should be attached to GitHub releases:

1. Create a release on GitHub
2. Attach package files:
   - `whisper-dictation-<version>-linux-x64.tar.gz`
   - `whisper-dictation_<version>_amd64.deb`
   - `whisper-dictation-<version>-x86_64.AppImage`
   - `whisper-dictation.flatpak`
   - `whisper-dictation-<version>-win-x64-portable.zip`
   - `WhisperDictation.Package_<version>_x64.msix`
   - `WhisperDictation.app` (as ZIP)

### Package Repositories

**Linux:**
- Submit to Flathub (Flatpak)
- Create PPA for Ubuntu (optional)
- Submit to AUR (Arch Linux, optional)

**Windows:**
- Submit to Microsoft Store (optional)
- Host on website for direct download

**macOS:**
- Submit Homebrew cask to homebrew/cask
- Host on website for direct download

## Troubleshooting

### Linux

**Problem:** AppImage won't run
```bash
# Check permissions
chmod +x whisper-dictation-*.AppImage

# Check FUSE
./whisper-dictation-*.AppImage --appimage-extract
# If FUSE not available, extract and run directly
```

**Problem:** Flatpak build fails
```bash
# Ensure all required runtimes are installed
flatpak list | grep org.freedesktop
flatpak install flathub org.freedesktop.Platform//24.08
```

### Windows

**Problem:** MSIX installation fails
```powershell
# Check Windows version (requires Windows 10 1703+)
winver

# Enable sideloading (if disabled)
# Settings → Update & Security → For developers → Sideload apps
```

### macOS

**Problem:** .app won't open
```bash
# Check code signature
codesign -dv --verbose=4 WhisperDictation.app

# If unsigned, sign with ad-hoc
codesign --force --deep --sign - WhisperDictation.app
```

## Contributing

When adding new packaging options:

1. Follow existing directory structure
2. Add build script with `--help` documentation
3. Update this README
4. Test on target platform
5. Add to CI/CD if possible

## License

All packaging scripts and specifications follow the same license as the main project.
