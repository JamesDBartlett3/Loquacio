# Loquacio - Development Specification

## Project Overview

**Project Name:** Loquacio
**Type:** Desktop Application (WPF / C#)
**Purpose:** Local Whisper-powered voice-to-text application for Windows with continuous and push-to-talk modes, local LLM post-processing, and system tray integration.

## User Story

As a Windows user who wants hands-free dictation or quick voice input, I need a local voice-to-text application that:
1. Runs entirely offline (no cloud API calls)
2. Supports continuous mode (hands-free) and push-to-talk mode (precise control)
3. Filters out filler words automatically
4. Corrects misheard words and adds punctuation via local LLM
5. Has a custom vocabulary for my domain-specific terminology
6. Can be activated via hotkey or keyword
7. Lives in the system tray and starts with Windows

So that I can dictate documents, code, notes, or messages quickly and accurately without privacy concerns or internet dependence.

## Requirements

### Functional Requirements

#### Audio Capture
- **Audio Source Selection:** List all available WASAPI audio input devices, allow user to select default or specific device
- **Calibration:** Test mic input level with visual VU meter, adjust gain if needed
- **Continuous Capture:** 24-bit/48kHz WASAPI capture to System.Threading.Channels queue (non-blocking)
- **Silence Detection:** Configurable silence threshold (default 1.5s) to trigger segment processing

#### Activation Modes
- **Continuous Mode (Default):**
  - App listens continuously
  - Silence threshold triggers processing
  - Immediately starts buffering next segment
  - Hands-free operation
  - Good for: long-form dictation, brainstorming, lectures
  - Cons: May pick up background noise

- **Push-to-Talk Mode:**
  - Hold hotkey to record, release to process
  - Precise control over what's captured
  - No background noise
  - Good for: noisy environments, short messages, code dictation
  - Cons: Requires a free hand

#### Whisper Processing
- **Model Selection:** User downloads/selects from Whisper model list (tiny, base, small, medium, large-v3)
- **Storage:** Models stored in `AppData\Roaming\Loquacio\models\`
- **Single Instance:** One Whisper.net processor instance processes segments sequentially from queue
- **Language Detection:** Auto-detect or user-specified language
- **Output:** Raw Whisper output as text segment

#### Post-Processing (Local LLM)
- **LLM Source:** Auto-detect LM Studio (:1234) or Ollama (:11434) via OpenAI SDK
- **Prompt Template:** "Correct misheard words, add appropriate punctuation, remove filler words. Return only the corrected text:\n\n{whisper_output}"
- **Concurrency:** LLM calls queued after Whisper completes (non-blocking)
- **Filler Word Filtering:** Skip/remove "um", "uh", "ehm", "ah", etc.
- **Punctuation:** Auto-add sentence-ending punctuation

#### Custom Vocabulary
- **Dictionary Format:** JSON file at `AppData\Roaming\Loquacio\custom-vocab.json`
- **Structure:** `{"terms": ["domain-specific-term1", "term2", ...]}`
- **Usage:** Whisper context boost (if supported) or LLM hinting in prompt
- **Editor:** Simple text editor in Settings UI for adding/removing terms

#### Output
- **Clipboard Mode (Default):** Auto-copy processed text to Windows clipboard
- **Type-Out Mode:** Simulate keystrokes to type into active window (optional)
- **History Log:** Save all processed segments to `AppData\Roaming\Loquacio\history.json`
- **Hotkey for Last Output:** User can re-copy last output with hotkey

#### Hotkeys
- **Global Activation:** User-configurable global hotkey (default: Ctrl+Alt+D)
- **Toggle Modes:** Switch between Continuous/Push-to-Talk (default: Ctrl+Alt+M)
- **Copy Last:** Copy last processed text (default: Ctrl+Alt+V)
- **Stop/Pause:** Stop listening (continuous mode) (default: Ctrl+Alt+S)

#### Keyword Activation
- **Engine:** Picovoice Porcupine (free for personal use)
- **Keyword:** User-configurable wake word (default: "Hey Dictate")
- **Action:** Toggles listening on/off when wake word detected
- **Privacy:** Local-only, no cloud calls

### Non-Functional Requirements

#### Performance
- **Audio Latency:** < 100ms from mic to queue entry
- **Processing Latency:** < 2s for 15-second segment (small model)
- **UI Responsiveness:** Never block UI thread (all processing on background threads)
- **Memory Usage:** < 500MB idle, < 2GB with medium model loaded

#### Reliability
- **Graceful Degradation:** If LLM unavailable, output Whisper raw text with notification
- **Error Recovery:** Audio capture restarts on mic disconnect/reconnect
- **Crash Recovery:** Auto-save history to disk every 10 seconds

#### Security & Privacy
- **Zero Cloud:** All processing local (Whisper + local LLM)
- **No Telemetry:** No network calls except for model downloads
- **Data Retention:** User controls history log retention

#### Usability
- **Tray Icon:** System tray icon with right-click context menu (Start, Stop, Settings, Quit)
- **Status Indicator:** Tray icon color changes: Gray (idle), Blue (listening), Yellow (processing), Red (error)
- **Toast Notifications:** Mode changes, errors, completions (configurable)

## Architecture

### Tech Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| **UI Framework** | WPF | .NET 8 |
| **MVVM Framework** | CommunityToolkit.Mvvm | 8.x |
| **Audio Capture** | NAudio | 2.x |
| **Speech Recognition** | Whisper.net | 1.9.1+ |
| **LLM Integration** | OpenAI SDK | Latest |
| **System Tray** | Hardcodet.NotifyIcon.Wpf | 2.0.1 |
| **Keyword Detection** | Porcupine | Latest |
| **Testing** | xUnit + NSubstitute | Latest |
| **Packaging** | MSIX | Windows App SDK |

### Component Architecture

```
Loquacio (WPF App)
├── Presentation (WPF Views + ViewModels)
│   ├── MainWindow.xaml (Settings, Model selection, Audio source)
│   ├── TrayIconViewModel (Tray icon, context menu)
│   ├── StatusViewModel (VU meter, processing status)
│   └── HistoryViewModel (History log viewer)
├── Services
│   ├── AudioCaptureService (WASAPI capture, Channels queue)
│   ├── WhisperProcessorService (Whisper.net, single instance)
│   ├── LLMPostProcessorService (LM Studio / Ollama via OpenAI SDK)
│   ├── HotkeyService (Global hotkey registration)
│   ├── KeywordDetectionService (Porcupine wake word)
│   ├── VocabularyService (Custom vocab JSON management)
│   ├── ClipboardService (Clipboard operations)
│   └── SettingsService (App configuration)
├── Models
│   ├── AudioSegment (raw audio data)
│   ├── TranscriptionResult (Whisper output + post-processed)
│   ├── Settings (app configuration)
│   └── CustomVocabulary (JSON model)
└── Infrastructure
    ├── Channels.cs (System.Threading.Channels queue)
    ├── App.xaml.cs (bootstrapper)
    └── ViewModelLocator (DI container via Microsoft.Extensions.DependencyInjection)
```

### Threading Model

| Thread | Purpose | Synchronization |
|--------|---------|-----------------|
| **UI Thread** | WPF rendering, user input | Dispatcher.Invoke for cross-thread calls |
| **Audio Capture Thread** | WASAPI capture → Channels | Producer (no lock needed with Channels) |
| **Whisper Processor Thread** | Read from queue → Whisper | Consumer (single instance) |
| **LLM Post-Processor Thread** | Whisper output → LLM → Clipboard | Sequential after Whisper |
| **Keyword Detection Thread** | Porcupine polling | Independent of audio capture |

### Data Flow

```
[Microphone]
    ↓ (WASAPI 24-bit/48kHz)
[AudioCaptureService]
    ↓ (System.Threading.Channels)
[AudioSegment Queue]
    ↓ (silence threshold triggered)
[WhisperProcessorService]
    ↓ (raw text)
[LLMPostProcessorService]
    ↓ (corrected text)
[ClipboardService]
    ↓
[Windows Clipboard / Active Window]
```

### Key Interfaces

```csharp
// Audio capture contract
public interface IAudioCaptureService
{
    event EventHandler<AudioSegment> OnSegmentCaptured;
    void StartCapture(AudioDevice device);
    void StopCapture();
}

// Whisper processing contract
public interface IWhisperProcessorService
{
    Task<string> ProcessAsync(AudioSegment segment);
    void LoadModel(string modelPath);
}

// LLM post-processing contract
public interface ILLMPostProcessorService
{
    Task<string> ProcessAsync(string whisperOutput, CustomVocabulary vocab);
    bool IsAvailable { get; }
}

// Output contract
public interface IOutputService
{
    void CopyToClipboard(string text);
    void TypeOut(string text); // Optional: keystroke simulation
}
```

### Settings Storage

- **Format:** JSON
- **Location:** `%AppData%\Loquacio\settings.json`
- **Schema:**
```json
{
  "audio": {
    "deviceId": "default",
    "gain": 1.0,
    "silenceThresholdMs": 1500
  },
  "whisper": {
    "modelPath": "C:\\Users\\...\\models\\ggml-tiny.bin",
    "language": "auto"
  },
  "llm": {
    "provider": "lm-studio", // "lm-studio" or "ollama"
    "endpoint": "http://localhost:1234/v1",
    "model": "auto"
  },
  "activation": {
    "mode": "continuous", // "continuous" or "push-to-talk"
    "hotkey": "Ctrl+Alt+D",
    "keywordEnabled": true,
    "keyword": "Hey Dictate"
  },
  "vocabulary": {
    "customWords": ["Power BI", "Fabric", "DAX"]
  },
  "output": {
    "mode": "clipboard", // "clipboard" or "typeout"
    "toastEnabled": true
  }
}
```

## Dependencies

### NuGet Packages

| Package | Purpose | License |
|---------|---------|---------|
| Whisper.net | Whisper inference engine | MIT |
| NAudio.Wasapi | Audio capture | MS-PL |
| Microsoft.Extensions.DependencyInjection | DI container | MIT |
| CommunityToolkit.Mvvm | MVVM helpers | MIT |
| Hardcodet.NotifyIcon.Wpf | System tray | MS-PL |
| OpenAI | LLM integration | MIT |
| Porcupine.NET | Keyword detection | Commercial (free for personal) |

### External Tools

| Tool | Purpose | License |
|------|---------|---------|
| WhisperGgmlDownloader (CLI) | Download Whisper models | MIT |
| LM Studio | Local LLM server | Proprietary (free) |
| Ollama | Local LLM server | MIT |

## Testing Strategy

### Unit Tests (xUnit)
- ViewModel logic (state machines, command binding)
- Settings serialization/deserialization
- Vocabulary management (JSON CRUD)
- Hotkey parsing/validation

### Integration Tests
- Audio capture → Channels → Whisper pipeline
- Whisper → LLM → Clipboard pipeline
- Settings UI ↔ SettingsService sync

### Regression Tests
- Silence threshold accuracy (different mic levels)
- LLM fallback when unavailable
- Hotkey registration after tray minimize/restore

### Performance Tests
- Memory usage over time (idle, listening, processing)
- Processing latency for each model size
- Thread starvation under continuous dictation

## Deployment

### MSIX Packaging
- **Windows App SDK** for modern packaging
- **Self-contained:** Include .NET 8 runtime (no user install needed)
- **Auto-update:** Check GitHub releases on startup (opt-in)
- **Installer:** Silent install with auto-start option

### Distribution
- **GitHub Releases:** MSIX installer + portable ZIP
- **License:** OSL 3.0 (open source)
- **Documentation:** README.md with screenshots, troubleshooting guide

---

**Version:** 1.0.0
**Status:** Planning Complete
**Next Phase:** Implementation (See PLAN.md)