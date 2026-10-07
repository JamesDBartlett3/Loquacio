# Cross-Platform Packaging (Phase D)

This directory contains packaging scripts and specifications for Loquacio across all supported platforms.

## Platform Coverage

| Platform | Formats | Status | Location |
|---|---|---|---|
| **Linux** | .deb, .tar.gz, .AppImage, Flatpak | ✅ Complete | `linux/` |
| **Windows** | MSIX, Portable .zip | ✅ Portable validated 2026-10-02 (cross-built on Linux); MSIX needs a Windows host | `windows/` |
| **macOS** | .app bundle (CI-built), Homebrew cask (spec) | ✅ .app built by CI | `macos/` |

## Linux Packaging (`linux/`)

### Quick Start

```bash
# Build all Linux packages
./packaging/linux/build-packages.sh

# Build only AppImage (requires base packages first)
./packaging/linux/build-appimage.sh

# Build Flatpak (requires flatpak installed)
flatpak-builder --repo=flatpak-repo flatpak-build packaging/linux/com.github.jamesdbartlett3.loquacio.json
```

### Package Formats

#### 1. Debian Package (.deb)

**Script:** `packaging/linux/build-packages.sh`

**Output:** `dist/loquacio_<version>_amd64.deb`

**Installation:**
```bash
sudo dpkg -i dist/loquacio_*_amd64.deb
systemctl --user enable --now loquacio-engine.service
```

**Features:**
- Installs to `/usr/lib/loquacio/`
- Wrapper scripts in `/usr/bin/`
- Systemd user service (`~/.config/systemd/user/`)
- Dependencies: `libasound2`, `pipewire` or `pulseaudio`

#### 2. Portable Tarball (.tar.gz)

**Script:** `packaging/linux/build-packages.sh`

**Output:** `dist/loquacio-<version>-linux-x64.tar.gz`

**Usage:**
```bash
tar -xzf loquacio-<version>-linux-x64.tar.gz
cd loquacio
./loquacio-engine
./loquacio-tui
./loquacio-avalonia
```

**Features:**
- Self-contained (no .NET runtime required)
- Can be run from any location
- AppImage-ready structure

#### 3. AppImage

**Script:** `packaging/linux/build-appimage.sh`

**Output:** `dist/loquacio-<version>-x86_64.AppImage`

**Usage:**
```bash
chmod +x loquacio-<version>-x86_64.AppImage
./loquacio-<version>-x86_64.AppImage
```

**Features:**
- Single-file executable
- Runs on any Linux distribution (glibc >= 2.17)
- Includes GUI launcher
- No installation required

#### 4. Flatpak

**Manifest:** `packaging/linux/com.github.jamesdbartlett3.loquacio.json`

**Build:**
```bash
flatpak install flathub org.freedesktop.Platform//24.08 org.freedesktop.Sdk//24.08 org.freedesktop.Platform.Dotnet//8.0
flatpak-builder --repo=flatpak-repo flatpak-build packaging/linux/com.github.jamesdbartlett3.loquacio.json
flatpak build-bundle flatpak-repo loquacio.flatpak com.github.jamesdbartlett3.loquacio
```

**Installation:**
```bash
flatpak install loquacio.flatpak
flatpak run com.github.jamesdbartlett3.loquacio
```

**Features:**
- Sandboxed environment
- Works across all Linux distributions
- Centralized updates via Flatpak

## Windows Validation (2026-10-02)

`build-portable.ps1` was executed on Linux (PowerShell 7.6.5, dotnet SDK 10.0.112) as a cross-publish validation:

- `dotnet publish -r win-x64 --self-contained` succeeded for Engine, WPF app, and TUI (0 errors)
- Artifact: `dist/loquacio-0.0.0.gitf17e126-win-x64-portable.zip` (86,067,634 bytes, 571 files)
- Contains loquacio-engine.exe, loquacio-tui.exe, Loquacio.exe (all PE32+ / MZ magic verified), plus Start/Install/Uninstall .bat helpers
- NOT executed (no Windows on this host): runtime behavior, Task Scheduler scripts, and MSIX packaging (build-msix.ps1 requires makeappx on Windows). Runtime verification on a Windows 11 host is the remaining follow-up.

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

**Output:** `dist/Loquacio.Package_<version>_x64.msix`

**Installation:**
```powershell
Add-AppxPackage .\dist\Loquacio.Package_*.msix
```

**Features:**
- Microsoft Store compatible
- Auto-update support
- Clean installation/uninstallation

#### 2. Portable ZIP

**Script:** `packaging/windows/build-portable.ps1`

**Output:** `dist/loquacio-<version>-win-x64-portable.zip`

**Usage:**
```powershell
# Extract and run
Expand-Archive .\loquacio-*-win-x64-portable.zip -DestinationPath .
.\Start Loquacio.bat

# Install engine as scheduled task
.\Install Engine.bat
```

**Features:**
- No installation required
- Includes Task Scheduler setup scripts
- All three controllers (WPF, TUI, Engine)

## macOS Packaging (`macOS/`)

**Note:** macOS packages can only be built on macOS. This directory contains specifications and build scripts that will work when run on macOS.

### Quick Start (on macOS)

```bash
# Build .app bundle
./packaging/macos/build-app.sh

# This will create:
# - dist/Loquacio.app
# - Launchd agent template
# - Code-signed application
```

### Package Format

#### .app Bundle

**Script:** `packaging/macos/build-app.sh` (macOS only)

**Output:** `dist/Loquacio.app`

**Installation:**
```bash
cp -R dist/Loquacio.app /Applications/
open /Applications/Loquacio.app
```

**Features:**
- Standard macOS application bundle
- CGEvent text injection
- Launchd agent support
- Requires accessibility and microphone permissions

### Homebrew Cask

**Formula:** `packaging/macos/loquacio.rb`

**Installation:**
```bash
brew install --cask loquacio
```

**CI/CD:**

GitHub Actions workflow: `.github/workflows/build-macos.yml`

Builds .app bundle on macOS runners and uploads as artifact.

## Auto-Update Mechanism

### Implementation

The auto-update service is implemented in:
- `Loquacio.Core/Services/IUpdateService.cs` - Interface
- `Loquacio.Engine/Services/GitHubUpdateService.cs` - GitHub-based implementation
- Registered in `Loquacio.Engine/EngineServiceRegistration.cs`

### How It Works

1. **Check for Updates:** Engine checks GitHub releases API on startup
2. **Download:** If update available, downloads package to temp directory
3. **Install:** Platform-specific installation:
   - **Linux:** Shows instructions to user (no silent install due to security)
   - **Windows:** Launches installer with elevation
   - **macOS:** Shows instructions to user

### Usage

The update service is automatically registered in the engine. To check for updates programmatically:

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
sudo dpkg -i dist/loquacio_*.deb
systemctl --user start loquacio-engine.service
/usr/bin/loquacio-tui

# Test AppImage
chmod +x dist/loquacio-*.AppImage
./dist/loquacio-*.AppImage

# Test Flatpak
flatpak install loquacio.flatpak
flatpak run com.github.jamesdbartlett3.loquacio
```

### Windows
```powershell
# Test portable
Expand-Archive .\dist\loquacio-*-win-x64-portable.zip -DestinationPath .\test
.\test\Start Loquacio.bat
```

### macOS (on macOS)
```bash
# Test .app
open dist/Loquacio.app
```

## Distribution

### GitHub Releases

All packages should be attached to GitHub releases:

1. Create a release on GitHub
2. Attach package files:
   - `loquacio-<version>-linux-x64.tar.gz`
   - `loquacio_<version>_amd64.deb`
   - `loquacio-<version>-x86_64.AppImage`
   - `loquacio.flatpak`
   - `loquacio-<version>-win-x64-portable.zip`
   - `Loquacio.Package_<version>_x64.msix`
   - `Loquacio.app` (as ZIP)

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
chmod +x loquacio-*.AppImage

# Check FUSE
./loquacio-*.AppImage --appimage-extract
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
codesign -dv --verbose=4 Loquacio.app

# If unsigned, sign with ad-hoc
codesign --force --deep --sign - Loquacio.app
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
