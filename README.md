# Loquacio

![Loquacio — local, private voice-to-text dictation](Assets/hero-banner.png)

Local Whisper-powered voice-to-text dictation that runs entirely on your machine — no cloud, no telemetry. A headless engine owns audio capture and transcription; controllers (Avalonia GUI, TUI, and a Windows WPF app) connect to it over a private IPC socket.

## Features

- **Fully local**: transcription via Whisper.net and optional LLM post-processing via a local LLM (LM Studio / Ollama) — nothing leaves your machine
- **Continuous Mode**: Listens continuously, processes on pause (~1.5s configurable silence threshold)
- **Push-to-Talk Mode**: Hold a hotkey to record, release to process
- **LLM Post-Processing**: Route raw transcription through a local LLM for auto-correction and punctuation
- **Custom Vocabulary**: Add uncommon words to improve recognition accuracy
- **Engine architecture**: start the engine once and attach any controller; the Avalonia GUI and TUI run on Linux, Windows, and macOS
- **System Tray Integration**: Minimize to tray, balloon notifications, quick-toggle listening
- **Auto-Start with Windows**: Optional launch at login
- **User-Configurable Models**: Download and switch between Whisper model sizes

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Speech Recognition | Whisper.net 1.9.1 |
| Engine / TUI / GUI | .NET 10, Avalonia 11.3 (GUI), custom console TUI |
| Windows Controller | WPF (.NET 10), MVVM via CommunityToolkit.Mvvm |
| Audio Capture | WASAPI via NAudio on Windows, PipeWire on Linux |
| IPC | Private Unix-domain socket (Linux) / named pipe (Windows) |
| LLM Integration | OpenAI SDK (LM Studio / Ollama compatible) |
| Testing | xUnit + NSubstitute (377 cross-platform tests, plus 18 WPF-only) |

## Installation

Download a build from the [releases page](https://github.com/JamesDBartlett3/Loquacio/releases) — all artifacts are self-contained (no .NET runtime required) and include the engine plus all controllers.

### Linux

| Format | Install |
|--------|---------|
| AppImage | `chmod +x loquacio-*-x86_64.AppImage && ./loquacio-*-x86_64.AppImage` |
| .deb | `sudo dpkg -i loquacio_*_amd64.deb` then `systemctl --user enable --now loquacio-engine.service` |
| .tar.gz | `tar -xzf loquacio-*-linux-x64.tar.gz` and run the binaries in place |

### Windows

- **Portable zip (recommended):** extract `loquacio-*-win-x64-portable.zip` and run `Start Loquacio.bat`; `Install Engine.bat` (run as administrator) registers the engine as a scheduled task for auto-start.
- **MSIX:** build locally with `./build-msix.ps1` on a Windows host (requires code signing for sideload).

### From Source

```bash
git clone https://github.com/JamesDBartlett3/Loquacio.git
cd loquacio

# Build on any platform (Windows, Linux, macOS)
dotnet build -c Release

# Run tests (works on Linux!)
dotnet test Loquacio.Tests -c Release
```

### Cross-Platform Development

The project is structured for cross-platform development:

| Project | Target | Builds on | Tests on |
|---------|--------|-----------|----------|
| `Loquacio.Core` | net10.0 | Any platform | ✅ Any platform |
| `Loquacio.Shared` | net10.0 | Any platform | ✅ Any platform |
| `Loquacio` | net10.0-windows (WPF) | Any platform* | Windows only |
| `Loquacio.Avalonia` | net10.0 | Any platform | ✅ Any platform |
| `Loquacio.Engine` | net10.0 | Any platform | ✅ Any platform |
| `Loquacio.Tui` | net10.0 | Any platform | ✅ Any platform |
| `Loquacio.Tests` | net10.0 | Any platform | ✅ Any platform |
| `Loquacio.Wpf.Tests` | net10.0-windows | Any platform* | Windows only |
| `TestConsole` | net10.0-windows | Any platform* | Windows only |
| `CrossPlatformTestConsole` / `SimpleCrossPlatformTestConsole` | net10.0 | Any platform | ✅ Any platform |

\* WPF projects compile on Linux/macOS via `<EnableWindowsTargeting>true</EnableWindowsTargeting>` (set in `Directory.Build.props`), but the resulting binary requires the Windows Desktop Runtime to execute.

```bash
# Quick build + test on Linux
./build.sh Release --test
```

### Building Packages

The release artifacts are produced by the scripts in [`packaging/`](packaging/README.md) — you don't need Visual Studio or any platform-specific IDE:

| Platform | Command (run from repo root) | Output |
|----------|------------------------------|--------|
| Linux | `./packaging/linux/build-packages.sh` | `.deb` + portable `.tar.gz` in `dist/` |
| Linux | `./packaging/linux/build-appimage.sh` | `.AppImage` in `dist/` |
| Windows | `pwsh packaging/windows/build-portable.ps1` | portable `.zip` in `dist/` (also cross-buildable from Linux/macOS via `packaging/windows/build-portable.sh`) |
| macOS | `./packaging/macos/build-app.sh` (on macOS) | ad-hoc-signed `Loquacio.app` in `dist/`; CI builds this on every push to `main` |

Each script publishes self-contained binaries (no .NET runtime required) before assembling the package. A Flatpak manifest is also available at `packaging/linux/com.github.jamesdbartlett3.loquacio.json`.

### MSIX (optional)

MSIX is a niche distribution path — mainly for Microsoft Store submission or enterprise sideloading. Build it from a Windows host with:

```powershell
./build-msix.ps1 -Configuration Release -Platform x64
# To sign the output:
./build-msix.ps1 -Sign -CertPath C:\certs\loquacio.pfx -CertPassword *****
```

Prerequisites: Windows 10/11, .NET 10 SDK, and the Windows SDK packaging/signing tools (or Visual Studio 2022). MSIX-mode caveats: auto-start uses the `windows.startupTask` manifest extension (the in-app toggle silently no-ops), and file access is sandboxed to `AppData\Local\Packages\<package-name>\`.

## Quick Start

1. **Launch** the app
2. Go to the **Whisper** tab and download a model (start with `base` for quick testing)
3. Go to the **Audio** tab and select your microphone
4. Click **Start** (or press `Ctrl+Alt+D`)
5. Speak — your words appear in the transcription box and are copied to your clipboard

## Activation Modes

| Mode | How It Works | Best For |
|------|-------------|----------|
| **Continuous** (default) | Listens always, processes on silence | Long-form dictation, hands-free |
| **Push-to-Talk** | Hold `Ctrl+Alt+D` to record | Noisy environments, precise control |

## Hotkeys

| Action | Default | Customizable |
|--------|---------|-------------|
| Toggle Listening | `Ctrl+Alt+D` | ✅ |
| Toggle Mode | `Ctrl+Alt+M` | ✅ |
| Stop | `Ctrl+Alt+S` | ✅ |
| Copy Last Output | `Ctrl+Alt+V` | ✅ |

## LLM Post-Processing

Route raw Whisper output through a local LLM for:
- Auto-punctuation and capitalization
- Filler word removal ("um", "uh", "ehm")
- Misheard word correction
- Custom vocabulary enforcement

**Supported Providers:**
- **LM Studio** (`http://localhost:1234/v1`) — default
- **Ollama** (`http://localhost:11434/v1`)

## System Tray

- **Minimize to tray**: Window hides, tray icon remains
- **Close to tray**: Closing the window hides it instead of exiting (configurable)
- **Double-click tray**: Restore window
- **Right-click tray**: Context menu (Show, Start/Stop, Settings, Exit)
- **Notifications**: Balloon tip on transcription completion (configurable)
- **Tray Icons**: State-aware icons (idle, listening, processing, error)
- **Single Instance**: Prevents multiple app instances from running simultaneously

## Project Structure

```
Loquacio.Core/       # Cross-platform class library (net10.0)
├── Models/                  # Data models (Settings, AudioSegment, etc.)
├── Infrastructure/          # Channels, cross-cutting concerns
├── Services/                # Cross-platform services + interfaces
│   ├── Whisper*             # Whisper.net processing
│   ├── LLM*                 # LLM post-processing, filler removal
│   ├── Settings*            # Settings persistence
│   ├── Vocabulary*          # Custom dictionary
│   ├── Background*          # Transcription orchestration
│   └── ActivationManager*   # Activation mode orchestration (keyword disabled)

Loquacio.Shared/     # Shared MVVM helpers (net10.0)

Loquacio/            # WPF application (net10.0-windows)
├── Services/                # Windows-specific services
│   ├── AudioCapture*        # WASAPI audio capture (NAudio)
│   ├── Hotkey*              # Global hotkeys (Win32)
│   ├── Tray*                # System tray (WPF)
│   ├── Clipboard*           # Clipboard output (Win32)
│   └── AutoStart*           # Registry auto-start
├── ViewModels/              # MVVM view models
├── Views/                   # WPF user controls (tabs)
├── Controls/                # Custom WPF controls (VU meter)
├── Converters/              # WPF value converters
├── App.xaml                 # Application entry point
└── MainWindow.xaml          # Main window

Loquacio.Engine/     # Headless engine (net10.0)
└── Services/Audio/          # WASAPI (Windows), PipeWire (Linux) capture

Loquacio.Avalonia/   # Avalonia GUI controller (net10.0, Linux/Windows/macOS)
Loquacio.Tui/        # Keyboard-only console controller (net10.0)

Loquacio.Tests/      # Cross-platform unit tests (net10.0)
Loquacio.Wpf.Tests/  # WPF-specific tests (net10.0-windows)
TestConsole/         # Windows-only test harness
CrossPlatformTestConsole/, SimpleCrossPlatformTestConsole/  # Console test harnesses
```

## Development Phases

| Phase | Status | Description |
|-------|--------|-------------|
| 1. Foundation | ✅ Complete | Project skeleton + audio capture pipeline |
| 2. Whisper Integration | ✅ Complete | Whisper.net + model management |
| 3. WPF UI | ✅ Complete | MVVM, settings, tabbed interface |
| 4. LLM + Vocabulary | ✅ Complete | LLM post-processing + custom vocabulary |
| 5. Hotkeys + Modes | ✅ Complete | Global hotkeys (all four customizable), activation modes (keyword activation is coded but disabled) |
| 6. Tray + Packaging | ✅ Complete | Tray integration, auto-start, single-instance; v0.1.0 ships Linux (AppImage/.deb/.tar.gz) and portable Windows (.zip). MSIX builds on a Windows host; CI builds an ad-hoc-signed macOS `.app` |

## Troubleshooting

### "No audio devices found"
- Ensure your microphone is connected and not in use by another application
- Check Windows privacy settings: **Settings → Privacy → Microphone → Allow apps to access your microphone**

### Poor transcription quality
- Try a larger model (small → medium → large-v3)
- Enable LLM post-processing for auto-correction
- Add domain-specific words to the custom vocabulary
- Speak clearly and minimize background noise

### High CPU usage
- Use a smaller model (tiny → base → small)
- Ensure you're not running other GPU-intensive applications
- Consider enabling LLM post-processing only for important dictation

## License

OSL 3.0 (Open Software License v3.0)

## Acknowledgments

- [Whisper.net](https://github.com/sandrohanea/whisper.net) — .NET bindings for OpenAI's Whisper
- [NAudio](https://github.com/naudio/NAudio) — .NET audio library
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) — WPF tray icon library
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM framework
