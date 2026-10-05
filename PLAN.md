# Loquacio - Implementation Plan

## Overview

This plan breaks down the Loquacio implementation into 6 phases over 4 innovation sessions (each ~2 hours). Each phase builds on the previous one, with incremental deliverables and verification steps.

## Phase Summary

| Phase | Duration | Deliverable | Sessions |
|-------|----------|-------------|----------|
| **Phase 1** | Session 1 (Part 1) | Project skeleton + audio capture pipeline | S1 |
| **Phase 2** | Session 1 (Part 2) | Whisper integration + basic transcription | S1 |
| **Phase 3** | Session 2 | WPF UI + MVVM + Settings | S2 |
| **Phase 4** | Session 3 | LLM post-processing + vocabulary | S3 |
| **Phase 5** | Session 4 (Part 1) | Hotkeys + keyword detection | S4 |
| **Phase 6** | Session 4 (Part 2) | Tray integration + MSIX packaging | S4 |

---

## Phase 1: Foundation (Project Skeleton + Audio Capture)

**Goal:** Set up project structure, WASAPI audio capture, and Channels queue.

**Session:** 1 (Part 1)
**Estimated Time:** 60 minutes

### Tasks

#### 1.1 Project Setup
- [ ] Create WPF project (`Loquacio.sln`)
- [ ] Target .NET 8
- [ ] Install NuGet packages:
  - `Whisper.net` (1.9.1+)
  - `NAudio.Wasapi` (2.x)
  - `Microsoft.Extensions.DependencyInjection`
  - `CommunityToolkit.Mvvm`
  - `Hardcodet.NotifyIcon.Wpf` (2.0.1)
  - `OpenAI` SDK
  - `xUnit` + `NSubstitute`
- [ ] Set up folder structure (Models, Services, ViewModels, Views, Infrastructure)
- [ ] Configure DI container in `App.xaml.cs`

**Verification:** Build succeeds, all packages resolve.

#### 1.2 Audio Capture Service
- [ ] Create `IAudioCaptureService` interface
- [ ] Implement `AudioCaptureService` using NAudio WasapiCapture
- [ ] Implement `OnSegmentCaptured` event for segment completion
- [ ] Test with physical microphone (capture 5s of audio)

**Verification:** Audio data captured and event fires.

#### 1.3 Channels Queue
- [ ] Create `AudioSegment` model (byte[] + duration + metadata)
- [ ] Set up `System.Threading.Channels` (Producer/Consumer)
- [ ] Producer: AudioCaptureService writes to channel on silence threshold
- [ ] Consumer: Placeholder reader (will be replaced by WhisperProcessorService)

**Verification:** AudioSegment objects flow from capture to consumer without blocking.

#### 1.4 Unit Tests
- [ ] Test `AudioSegment` model (serialization, properties)
- [ ] Mock audio capture for testing without physical mic

**Verification:** All tests pass.

---

## Phase 2: Whisper Integration

**Goal:** Integrate Whisper.net for transcription and output raw text.

**Session:** 1 (Part 2)
**Estimated Time:** 60 minutes

### Tasks

#### 2.1 Whisper Processor Service
- [ ] Create `IWhisperProcessorService` interface
- [ ] Implement `WhisperProcessorService`:
  - Load Whisper model from path
  - Process `AudioSegment` → string transcription
  - Single instance (sequential processing)
- [ ] Integrate with Channels consumer (replace placeholder)

**Verification:** Audio from capture → Whisper → text output.

#### 2.2 Model Management
- [ ] Implement `WhisperGgmlDownloader` CLI integration
- [ ] Create `ModelManagerService` for model selection/download
- [ ] Store models in `AppData\Roaming\Loquacio\models\`
- [ ] UI to select model (tiny, base, small, medium, large-v3)

**Verification:** User can download and switch between models.

#### 2.3 Console App Testing
- [ ] Create console app for rapid testing (no UI yet)
- [ ] Test capture → Whisper pipeline end-to-end
- [ ] Verify output quality with different models

**Verification:** Dictation accuracy acceptable for testing.

#### 2.4 Unit Tests
- [ ] Test WhisperProcessorService with mock audio data
- [ ] Test ModelManagerService path resolution

**Verification:** All tests pass.

---

## Phase 3: WPF UI + MVVM + Settings

**Goal:** Build the main WPF UI with MVVM pattern and settings persistence.

**Session:** 2
**Estimated Time:** 120 minutes

### Tasks

#### 3.1 Main Window UI
- [ ] Create `MainWindow.xaml` with tabs:
  - **General Tab:** Mode selection, hotkey configuration, status
  - **Audio Tab:** Device selection, gain, silence threshold
  - **Whisper Tab:** Model selection, language
  - **LLM Tab:** Provider selection (LM Studio / Ollama), endpoint
  - **Vocabulary Tab:** Custom word editor
- [ ] Create `MainWindowViewModel` with CommunityToolkit.Mvvm
- [ ] Bind UI controls to ViewModel properties (INotifyPropertyChanged)

**Verification:** UI renders, controls bind correctly, no binding errors in Output window.

#### 3.2 Settings Service
- [ ] Create `ISettingsService` interface
- [ ] Implement `SettingsService` with JSON serialization
  - Load/save to `%AppData%\Loquacio\settings.json`
  - Validate settings on load
  - Raise `SettingsChanged` event
- [ ] Integrate with ViewModel (two-way binding)

**Verification:** Settings persist across app restarts.

#### 3.3 VU Meter
- [ ] Create `VUMeterControl.xaml` (visual audio level indicator)
- [ ] Bind to `AudioCaptureService.OnAudioLevel` event
- [ ] Update in real-time (use Dispatcher.BeginInvoke to avoid UI blocking)

**Verification:** VU meter responds to mic input in real-time.

#### 3.4 Status Indicator
- [ ] Create status display (Idle / Listening / Processing / Error)
- [ ] Use color-coded icons (Gray / Blue / Yellow / Red)
- [ ] Update based on service state

**Verification:** Status changes correctly during capture/processing.

#### 3.5 Unit Tests
- [ ] Test SettingsService serialization/deserialization
- [ ] Test ViewModel property change notifications
- [ ] Test VU meter value updates

**Verification:** All tests pass.

---

## Phase 4: LLM Post-Processing + Vocabulary

**Goal:** Add local LLM integration for correction and custom vocabulary support.

**Session:** 3
**Estimated Time:** 120 minutes

### Tasks

#### 4.1 LLM Post-Processor Service
- [ ] Create `ILLMPostProcessorService` interface
- [ ] Implement `LLMPostProcessorService`:
  - Auto-detect LM Studio (:1234) or Ollama (:11434)
  - Use OpenAI SDK (compatible with both)
  - Prompt template: "Correct misheard words, add punctuation, remove filler words:\n\n{whisper_output}"
  - Queue LLM calls after Whisper completes
  - Graceful fallback: output raw Whisper text if LLM unavailable

**Verification:** Whisper output → LLM → corrected text.

#### 4.2 LLM Configuration UI
- [ ] Add LLM settings to MainWindow LLM tab:
  - Provider dropdown (Auto-detect / LM Studio / Ollama)
  - Endpoint URL (default: http://localhost:1234/v1)
  - Model name (default: auto)
  - Test Connection button

**Verification:** User can configure LLM, connection test succeeds.

#### 4.3 Vocabulary Service
- [ ] Create `IVocabularyService` interface
- [ ] Implement `VocabularyService`:
  - Load/save custom words from JSON
  - Add/remove words
  - Inject vocabulary into LLM prompt as context

**Verification:** Custom words influence LLM corrections.

#### 4.4 Clipboard Service
- [ ] Create `IClipboardService` interface
- [ ] Implement `ClipboardService`:
  - Copy text to Windows clipboard
  - Optional: Type-out mode (keystroke simulation)

**Verification:** Processed text appears in clipboard.

#### 4.5 Integration Testing
- [ ] End-to-end: Mic → Whisper → LLM → Clipboard
- [ ] Test with filler words ("um", "uh") removed
- [ ] Test with custom vocabulary (domain-specific terms)

**Verification:** Final output is clean, punctuated, and correct.

#### 4.6 Unit Tests
- [ ] Test LLMPostProcessorService with mock LLM
- [ ] Test VocabularyService CRUD operations
- [ ] Test ClipboardService (may need UI automation for clipboard)

**Verification:** All tests pass.

---

## Phase 5: Hotkeys + Keyword Detection

**Goal:** Add global hotkey support and Porcupine keyword detection.

**Session:** 4 (Part 1)
**Estimated Time:** 60 minutes

### Tasks

#### 5.1 Hotkey Service
- [ ] Create `IHotkeyService` interface
- [ ] Implement `HotkeyService`:
  - Register global hotkeys (Win32 RegisterHotKey)
  - Hotkey: Ctrl+Alt+D (toggle listening)
  - Hotkey: Ctrl+Alt+M (toggle mode)
  - Hotkey: Ctrl+Alt+V (copy last output)
  - Hotkey: Ctrl+Alt+S (stop)
  - Unregister on app exit

**Verification:** Hotkeys trigger correct actions from any app.

#### 5.2 Keyword Detection Service
- [ ] Create `IKeywordDetectionService` interface
- [ ] Implement `KeywordDetectionService` with Porcupine:
  - Load wake word model
  - Poll audio stream for wake word
  - Trigger toggle on detection
  - Configurable keyword (default: "Hey Dictate")

**Verification:** Saying "Hey Dictate" toggles listening on/off.

#### 5.3 Hotkey Configuration UI
- [ ] Add hotkey editor to MainWindow:
  - Grid with hotkey name, current binding, edit button
  - Capture keypress on edit (show "Press a key..." dialog)

**Verification:** User can customize hotkeys.

#### 5.4 Unit Tests
- [ ] Test HotkeyService registration/unregistration
- [ ] Test KeywordDetectionService with mock audio

**Verification:** All tests pass.

---

## Phase 6: Tray Integration + MSIX Packaging

**Goal:** Add system tray icon, finalize UI, and package as MSIX installer.

**Session:** 4 (Part 2)
**Estimated Time:** 60 minutes

### Tasks

#### 6.1 System Tray Integration
- [ ] Create `TrayIconViewModel`:
  - Tray icon with color-coded status (Gray/Blue/Yellow/Red)
  - Right-click context menu: Start, Stop, Settings, Quit
  - NotifyIcon from Hardcodet.NotifyIcon.Wpf

**Verification:** Tray icon appears, context menu works.

#### 6.2 Minimize to Tray
- [ ] Override MainWindow OnStateChanged:
  - Minimize → hide window, show tray icon
  - Double-click tray icon → restore window

**Verification:** App minimizes to tray, restores correctly.

#### 6.3 Auto-Start with Windows
- [ ] Add checkbox to Settings: "Start with Windows"
- [ ] Create/Run registry key: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- [ ] Remove on uncheck

**Verification:** App launches on Windows login when enabled.

#### 6.4 History Log
- [ ] Implement `HistoryService`:
  - Save all processed segments to `history.json`
  - Limit to last 1000 entries (rotate)
  - Provide history viewer in UI

**Verification:** History persists, viewer loads correctly.

#### 6.5 MSIX Packaging
- [ ] Add Windows App SDK package project
- [ ] Configure manifest (app identity, capabilities, min version)
- [ ] Self-contained .NET 8 runtime (no user install)
- [ ] Test installation/uninstallation on Windows 11

**Verification:** MSIX installs, runs, uninstalls cleanly.

#### 6.6 Documentation
- [ ] Write `README.md`:
  - Installation instructions
  - Quick start guide
  - Screenshots of UI
  - Troubleshooting section
- [x] Add LICENSE file (OSL 3.0)

**Verification:** README is comprehensive and accurate.

#### 6.7 Final Testing
- [ ] Smoke test: Install → Start → Dictate → Verify output
- [ ] Regression test: All phases still work end-to-end
- [ ] Performance test: Memory usage < 500MB idle, < 2GB with model

**Verification:** App works as specified in SPEC.md.

---

## Session Allocation

| Session | Phases | Focus | Deliverable |
|---------|--------|-------|-------------|
| **Session 1** | 1-2 | Audio capture + Whisper | Console app prototype |
| **Session 2** | 3 | WPF UI + Settings | Main window with tabs |
| **Session 3** | 4 | LLM + Vocabulary | Corrected text output |
| **Session 4** | 5-6 | Hotkeys + Tray + Package | Complete app with MSIX |

---

## Risk Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Whisper.net API changes** | High | Pin to specific version (1.9.1), monitor releases |
| **LLM endpoint changes** | Medium | Auto-detect + manual config option |
| **Audio capture issues on some hardware** | Medium | Fallback to default device, provide calibration UI |
| **MSIX packaging issues** | Low | Test on Windows 10 and 11, use Windows App SDK |
| **Porcupine licensing** | Low | Free for personal use, document commercial license requirements |

---

## Exit Criteria

The project is **complete** when:
1. [ ] All 6 phases are implemented and tested
2. [ ] MSIX installer installs and runs on Windows 10/11
3. [ ] End-to-end dictation works (mic → Whisper → LLM → clipboard)
4. [ ] Hotkeys work globally
5. [ ] Keyword detection works
6. [ ] Settings persist across restarts
7. [ ] Memory usage meets spec (< 500MB idle, < 2GB with model)
8. [ ] README.md is complete
9. [ ] GitHub repo created and pushed

---

**Version:** 1.0.0
**Status:** Ready for Implementation
**Next Action:** Begin Session 1, Phase 1