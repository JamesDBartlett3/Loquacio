# macOS Packaging (Phase D) - Specification

This document specifies the macOS packaging approach for Loquacio. macOS packages must be built on macOS (or in a macOS CI runner).

## Package Format

### Primary: .app bundle

The main distribution format is a self-contained .app bundle that includes:
- All three controllers (Avalonia GUI, TUI, Daemon)
- All .NET runtime dependencies
- System configuration files
- CGEvent text injection implementation

### Secondary: Homebrew cask

A Homebrew cask formula for easy installation via `brew install --cask`.

## .app Bundle Structure

```
Loquacio.app/
├── Contents/
│   ├── Info.plist
│   ├── MacOS/
│   │   └── loquacio (launcher script)
│   ├── Resources/
│   │   ├── app-icon.icns
│   │   └── daemon.plist (launchd agent)
│   └── Frameworks/
│       └── (all .NET runtime and app binaries)
```

## Key Implementation Details

### 1. CGEvent Text Injection

The daemon on macOS uses `CGEvent` from the Application Services framework to inject text into the focused window.

**Implementation location:** `Loquacio.Daemon/Services/TextInjectionService.cs` (`InjectMacOSAsync` + CGEvent P/Invokes) — **IMPLEMENTED** (2026-09-06). Compiles on any OS; CGEvent calls execute only on macOS. Chunk-planning logic (`SplitMacOSUnicodeChunks`) is internal and unit-tested cross-platform.

Clipboard mode uses `pbcopy` + Cmd+V (CGEvent); direct mode types Unicode via `CGEventKeyboardSetUnicodeString` in ≤20-code-unit chunks with `\n` mapped to Return keystrokes.

**Remaining untested on real hardware** (no macOS available to Cray): requires James to run the daemon on a Mac with Accessibility permission granted.

**P/Invoke signatures needed:**
```csharp
[DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
public static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort keyCode, bool keyDown);

[DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
public static extern void CGEventKeyboardSetUnicodeString(IntPtr eventRef, int length, IntPtr[] unicodeString);

[DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
public static extern void CGEventPost(uint location, IntPtr eventRef);
```

### 2. launchd Agent for Daemon Autostart

**Location:** `~/Library/LaunchAgents/com.github.jamesdbartlett3.loquacio.daemon.plist`

**Content:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.github.jamesdbartlett3.loquacio.daemon</string>
    <key>ProgramArguments</key>
    <array>
        <string>/Applications/Loquacio.app/Contents/Frameworks/loquacio-daemon</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>~/Library/Logs/Loquacio/daemon.log</string>
    <key>StandardErrorPath</key>
    <string>~/Library/Logs/Loquacio/daemon-error.log</string>
    <key>ProcessType</key>
    <string>Interactive</string>
</dict>
</plist>
```

**Installation:** `packaging/macos/build-app.sh` already generates the resolved-path plist into `Contents/Resources/` at build time. **DONE.** The GUI controller should still offer to install it into `~/Library/LaunchAgents/` via a permissions dialog.

### 3. Global Hotkeys on macOS

**Approach:** Use `NSEvent` global event monitoring in the daemon.

**Implementation:** `Loquacio.Daemon/Platform/macOS/MacOSHotkeyService.cs` — **still spec-only** (needs a Mac to develop against).

**Requirements:**
- Add reference to `AppKit` framework
- Use `NSEvent.AddGlobalMonitorForEventsMatchingMask` for hotkey detection
- Request accessibility permissions via GUI prompt on first use

**Permissions:** Requires "Accessibility" permission in System Settings → Privacy & Security.

### 4. Audio Capture on macOS

**Approach:** Use `AVFoundation` for audio capture.

**Implementation:** `Loquacio.Daemon/Platform/macOS/MacOSAudioCaptureService.cs`

**Requirements:**
- Add reference to `AVFoundation` framework
- Request microphone permissions via GUI prompt
- Capture 16kHz/16-bit/mono PCM

### 5. Bundle Build Script

**Location:** `packaging/macos/build-app.sh`

**Key steps:**
1. Publish all three projects for `osx-x64` RID
2. Create .app bundle structure
3. Copy binaries to `Contents/Frameworks/`
4. Generate `Info.plist`
5. Create launcher script in `Contents/MacOS/`
6. Generate `.icns` icon
7. Code sign with ad-hoc signature (for distribution)

## Build Requirements

- macOS 12 (Monterey) or later
- .NET 10.0 SDK
- Xcode Command Line Tools (for codesign)

## CI Integration

### GitHub Actions Workflow

**File:** `.github/workflows/build-macos.yml`

```yaml
name: Build macOS Package

on:
  push:
    branches: [main, dev]
    tags: ['v*']
  pull_request:
    branches: [main, dev]

jobs:
  build:
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Publish binaries
        run: |
          dotnet publish Loquacio.Daemon/Loquacio.Daemon.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=false -o dist/publish/osx-x64
          dotnet publish Loquacio.Avalonia/Loquacio.Avalonia.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=false -o dist/publish/osx-x64
          dotnet publish Loquacio.Tui/Loquacio.Tui.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=false -o dist/publish/osx-x64

      - name: Build .app bundle
        run: packaging/macos/build-app.sh

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: loquacio-macos
          path: dist/*.app
```

## Testing Checklist

- [ ] Daemon starts via launchd agent
- [ ] Audio capture works (microphone permission granted)
- [ ] Global hotkey detection works (accessibility permission granted)
- [ ] CGEvent text injection into focused window works
- [ ] Avalonia GUI controller launches and connects to daemon
- [ ] Settings persist across restarts
- [ ] App bundle passes Gatekeeper (ad-hoc signature)
- [ ] Homebrew cask installs and launches correctly

## Known Limitations

1. **Sandboxing:** The .app bundle cannot be sandboxed because it needs:
   - Global hotkey monitoring (accessibility)
   - Text injection into other apps (CGEvent)
   - Network access (for optional LLM post-processing)

2. **Code signing:** Distribution requires Apple Developer certificate. For now, use ad-hoc signature which works but shows warning on first launch.

3. **Notarization:** For distribution outside Mac App Store, the app must be notarized by Apple. This requires:
   - Apple Developer account ($99/year)
   - Valid code signing certificate
   - Upload to Apple for notarization
   - Stapling notarization ticket to app bundle

## Next Steps

1. Implement macOS platform services in `Loquacio.Daemon/Platform/macOS/`
2. Create `packaging/macos/build-app.sh` script
3. Test on real macOS hardware
4. Set up GitHub Actions for macOS builds
5. Create Homebrew cask formula
6. (Optional) Obtain Apple Developer certificate for code signing and notarization

## Version History

- **2026-08-27:** Initial specification created
