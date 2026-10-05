# Whisper Dictation - Phase 6 End-to-End Testing Plan

## Overview

This document provides the comprehensive end-to-end testing checklist for Phase 6 (Tray Integration + MSIX Packaging) of the Whisper Dictation application.

**Note:** Tests must be executed on Windows 10 (version 22621+) or Windows 11. Linux cannot run this WPF application.

## Prerequisites

- Windows 10 (build 22621) or Windows 11
- Whisper model downloaded (tiny, base, or small recommended for testing)
- Local LLM endpoint running (LM Studio on port 1234 or Ollama on port 11434) - optional but recommended
- Microphone connected and functional

---

## Phase 6.1: System Tray Integration Tests

### Test 6.1.1: Tray Icon Appears on Launch

**Steps:**
1. Launch the application
2. Check Windows system tray area

**Expected Result:**
- [ ] Tray icon appears in the notification area
- [ ] Icon shows idle state (gray color)
- [ ] Tooltip displays "Whisper Dictation - Idle"

---

### Test 6.1.2: Double-Click Tray Icon

**Steps:**
1. Launch application
2. Minimize main window to tray
3. Double-click tray icon

**Expected Result:**
- [ ] Main window restores from minimized state
- [ ] Window receives focus
- [ ] Tray icon remains visible

---

### Test 6.1.3: Right-Click Context Menu - Show Window

**Steps:**
1. Launch application
2. Minimize main window
3. Right-click tray icon
4. Click "Show Window"

**Expected Result:**
- [ ] Context menu appears
- [ ] "Show Window" menu item is present
- [ ] Main window restores
- [ ] Context menu closes

---

### Test 6.1.4: Right-Click Context Menu - Start/Stop Listening

**Steps:**
1. Launch application
2. Right-click tray icon
3. Click "Start Listening"
4. Verify listening state
5. Right-click tray icon
6. Click "Stop Listening"
7. Verify stopped state

**Expected Result:**
- [ ] Context menu shows "Start Listening" when not listening
- [ ] Context menu shows "Stop Listening" when listening
- [ ] Clicking toggles listening state correctly
- [ ] Tray icon color changes (gray → blue for listening)

---

### Test 6.1.5: Right-Click Context Menu - Settings

**Steps:**
1. Launch application
2. Right-click tray icon
3. Click "Settings..."

**Expected Result:**
- [ ] Settings dialog/window opens
- [ ] Can modify settings
- [ ] Settings persist when closed

---

### Test 6.1.6: Right-Click Context Menu - Exit

**Steps:**
1. Launch application
2. Right-click tray icon
3. Click "Exit"

**Expected Result:**
- [ ] Application closes cleanly
- [ ] Tray icon disappears
- [ ] No error messages
- [ ] Settings saved before exit

---

### Test 6.1.7: Tray Icon State Updates

**Steps:**
1. Launch application
2. Start listening
3. Speak for 5-10 seconds
4. Stop listening
5. Intentionally cause error (e.g., select invalid audio device)

**Expected Result:**
- [ ] Icon shows gray for idle state
- [ ] Icon shows blue for listening state
- [ ] Icon shows yellow for processing state (during transcription)
- [ ] Icon shows red for error state

---

### Test 6.1.8: Balloon Notifications

**Steps:**
1. Launch application
2. Start listening
3. Wait for transcription completion
4. Cause error (invalid settings)

**Expected Result:**
- [ ] Notification appears when dictation ready (if implemented)
- [ ] Notification appears on error
- [ ] Notification has correct title and message
- [ ] Notification disappears automatically or on click

---

## Phase 6.2: Auto-Start at Login Tests

### Test 6.2.1: Enable Auto-Start via Settings

**Steps:**
1. Launch application
2. Open Settings > General tab
3. Check "Start with Windows"
4. Close application

**Expected Result:**
- [ ] Registry key created: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WhisperDictation`
- [ ] Registry value points to application executable
- [ ] Application starts on next Windows login

---

### Test 6.2.2: Disable Auto-Start via Settings

**Steps:**
1. Launch application with auto-start enabled
2. Open Settings > General tab
3. Uncheck "Start with Windows"
4. Close application

**Expected Result:**
- [ ] Registry key removed
- [ ] Application does NOT start on next Windows login

---

### Test 6.2.3: Auto-Start Launches Minimized

**Steps:**
1. Enable auto-start
2. Log out of Windows
3. Log back in

**Expected Result:**
- [ ] Application launches automatically
- [ ] Application starts in minimized state (to tray)
- [ ] Tray icon appears
- [ ] Main window does NOT open

---

### Test 6.2.4: Auto-Start State Persistence

**Steps:**
1. Enable auto-start
2. Close application
3. Reopen application
4. Check Settings > General tab

**Expected Result:**
- [ ] "Start with Windows" checkbox remains checked
- [ ] Setting persisted across application restart

---

### Test 6.2.5: Registry Key Validation

**Steps:**
1. Enable auto-start
2. Open Registry Editor
3. Navigate to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

**Expected Result:**
- [ ] `WhisperDictation` key exists
- [ ] Value is `"C:\path\to\WhisperDictation.exe" --minimized`
- [ ] Value type is REG_SZ

---

## Phase 6.3: MSIX Packaging Tests

### Test 6.3.1: MSIX Build

**Steps:**
1. Open Visual Studio on Windows
2. Open solution file
3. Select `WhisperDictation.Package` project
4. Build for x64 platform

**Expected Result:**
- [ ] Build succeeds without errors
- [ ] `.msix` file generated in output directory
- [ ] File size is reasonable (< 500MB with bundled .NET runtime)

---

### Test 6.3.2: MSIX Installation

**Steps:**
1. Right-click generated `.msix` file
2. Select "Install"

**Expected Result:**
- [ ] Installation dialog appears
- [ ] No capability permission prompts (only safe capabilities used)
- [ ] Installation completes successfully
- [ ] Application appears in Start Menu

---

### Test 6.3.3: Launch from Start Menu

**Steps:**
1. Open Start Menu
2. Search for "Whisper Dictation"
3. Click to launch

**Expected Result:**
- [ ] Application launches
- [ ] Tray icon appears
- [ ] Main window opens
- [ ] Application is fully functional

---

### Test 6.3.4: MSIX App Identity

**Steps:**
1. Install MSIX package
2. Right-click installed app in Start Menu
3. Select "App Settings"

**Expected Result:**
- [ ] Publisher name: "WhisperDictation"
- [ ] Display name: "Whisper Dictation"
- [ ] Version: 1.0.0.0

---

### Test 6.3.5: MSIX Uninstallation

**Steps:**
1. Open Settings > Apps > Installed Apps
2. Find "Whisper Dictation"
3. Click "Uninstall"

**Expected Result:**
- [ ] Uninstall dialog appears
- [ ] Uninstall completes without errors
- [ ] App removed from Start Menu
- [ ] Registry keys cleaned up
- [ ] AppData folder optionally removed (or left with user data)

---

### Test 6.3.6: MSIX Store Logo and Icons

**Steps:**
1. Install MSIX package
2. View app in Start Menu (various tile sizes)
3. Check taskbar and desktop icon

**Expected Result:**
- [ ] Store logo (50x50) displays correctly
- [ ] Small tile (71x71) displays correctly
- [ ] Medium tile (150x150) displays correctly
- [ ] Wide tile (310x150) displays correctly
- [ ] Large tile (310x310) displays correctly
- [ ] Splash screen (620x300) appears during launch

---

### Test 6.3.7: MSIX Capabilities

**Steps:**
1. Install MSIX package
2. Launch application
3. Use microphone for dictation
4. Check app capabilities in PowerShell

```powershell
Get-AppxPackage -Name Innovation.WhisperDictation | Select-Object -ExpandProperty Capabilities
```

**Expected Result:**
- [ ] Microphone access works (required for dictation)
- [ ] No unnecessary capabilities requested
- [ ] Only `backgroundMediaRecording` and `internetClient` capabilities present

---

### Test 6.3.8: MSIX Version Update

**Steps:**
1. Install version 1.0.0.0
2. Update version to 1.0.1.0 in manifest
3. Rebuild MSIX
4. Install newer version

**Expected Result:**
- [ ] Installation succeeds
- [ ] App updates in place (preserves settings)
- [ ] No data loss
- [ ] Version number updates in App Settings

---

## Phase 6.4: Integration Tests (All Phase 6 Features Combined)

### Test 6.4.1: Full Workflow - Tray + Auto-Start

**Steps:**
1. Enable auto-start
2. Close application
3. Log out and log back in
4. Verify app launched minimized
5. Double-click tray icon to show window
6. Start dictation via tray context menu
7. Speak for 10 seconds
8. Stop dictation via tray context menu

**Expected Result:**
- [ ] Auto-start works on login
- [ ] App starts minimized to tray
- [ ] Tray icon shows correct state
- [ ] All tray menu items work
- [ ] Dictation starts/stops correctly from tray

---

### Test 6.4.2: Full Workflow - MSIX Installation + Daily Use

**Steps:**
1. Install fresh MSIX package
2. Launch from Start Menu
3. Configure initial settings (audio device, model, language)
4. Enable auto-start
5. Minimize to tray
6. Perform dictation tasks throughout the day
7. Use hotkeys (Ctrl+Alt+D to toggle)
8. Close and reopen app multiple times
9. Eventually uninstall

**Expected Result:**
- [ ] Installation works cleanly
- [ ] Settings persist across sessions
- [ ] Tray integration works reliably
- [ ] Auto-start works on subsequent logins
- [ ] No memory leaks after prolonged use
- [ ] Uninstall removes cleanly

---

### Test 6.4.3: Error Handling - Tray Notifications

**Steps:**
1. Configure invalid settings (e.g., non-existent audio device)
2. Attempt to start listening
3. Check tray icon and notifications

**Expected Result:**
- [ ] Tray icon shows error state (red)
- [ ] Notification appears with error message
- [ ] Application remains stable
- [ ] Can recover by correcting settings

---

### Test 6.4.4: Multi-Instance Prevention

**Steps:**
1. Launch application
2. Attempt to launch second instance (click Start Menu again)

**Expected Result:**
- [ ] Second instance does not launch OR
- [ ] First instance gets focus (single-instance enforcement)
- [ ] No error messages or crashes

---

### Test 6.4.5: System Shutdown with Tray App

**Steps:**
1. Launch application
2. Minimize to tray
3. Initiate Windows shutdown/restart

**Expected Result:**
- [ ] Application closes cleanly
- [ ] No hanging processes
- [ ] No Windows error about app blocking shutdown
- [ ] Settings saved

---

## Regression Tests (Verify Phases 1-5 Still Work)

### Test R.1: Audio Capture (Phase 1)

**Steps:**
1. Launch application
2. Select microphone in Settings > Audio
3. Start listening
4. Speak for 5 seconds
5. Stop listening

**Expected Result:**
- [ ] VU meter responds to audio input
- [ ] Audio captured and sent to processing pipeline

---

### Test R.2: Whisper Transcription (Phase 2)

**Steps:**
1. Ensure Whisper model is loaded
2. Start listening
3. Speak clear text
4. Stop listening
5. Check output

**Expected Result:**
- [ ] Transcription appears
- [ ] Text is reasonably accurate
- [ ] No errors during Whisper processing

---

### Test R.3: Settings Persistence (Phase 3)

**Steps:**
1. Modify various settings (audio, Whisper, LLM)
2. Close application
3. Reopen application
4. Verify settings are saved

**Expected Result:**
- [ ] All settings persist across restart
- [ ] No settings lost or corrupted

---

### Test R.4: LLM Post-Processing (Phase 4)

**Steps:**
1. Configure LLM endpoint (LM Studio or Ollama)
2. Enable LLM post-processing
3. Start listening
4. Speak with filler words ("um", "uh")
5. Stop listening
6. Compare raw output vs corrected output

**Expected Result:**
- [ ] LLM corrects transcription
- [ ] Filler words removed
- [ ] Punctuation added
- [ ] Output is cleaner than raw Whisper

---

### Test R.5: Hotkeys (Phase 5)

**Steps:**
1. Launch application
2. Press Ctrl+Alt+D to toggle listening
3. Press Ctrl+Alt+S to stop
4. Press Ctrl+Alt+M to toggle mode

**Expected Result:**
- [ ] Hotkeys work globally (from any app)
- [ ] Each hotkey triggers correct action
- [ ] Hotkeys work even when app is minimized to tray

---

### Test R.6: Keyword Detection (Phase 5)

**Steps:**
1. Configure wake word (default: "Hey Dictate")
2. Start app idle
3. Speak "Hey Dictate"
4. Verify listening toggles on

**Expected Result:**
- [ ] Wake word detected
- [ ] Listening toggles on automatically
- [ ] No false positives during normal conversation

---

## Performance Tests

### Test P.1: Memory Usage - Idle

**Steps:**
1. Launch application
2. Keep idle for 5 minutes
3. Check Task Manager memory usage

**Expected Result:**
- [ ] Memory usage < 500MB when idle
- [ ] No memory leaks over time

---

### Test P.2: Memory Usage - With Model

**Steps:**
1. Load Whisper model (small or medium)
2. Keep app running with model loaded
3. Check Task Manager memory usage

**Expected Result:**
- [ ] Memory usage < 2GB with model loaded
- [ ] Memory stable over time

---

### Test P.3: Startup Time

**Steps:**
1. Measure time from Start Menu click to window open
2. Repeat 5 times, take average

**Expected Result:**
- [ ] Startup time < 10 seconds (with model cached)
- [ ] Startup time < 30 seconds (first run, model not cached)

---

### Test P.4: Dictation Latency

**Steps:**
1. Start listening
2. Speak a 5-second phrase
3. Stop listening
4. Measure time to transcription completion

**Expected Result:**
- [ ] Latency < 5 seconds for tiny model
- [ ] Latency < 10 seconds for base model
- [ ] Latency < 15 seconds for small model

---

## Security Tests

### Test S.1: MSIX Signing (If Signed)

**Steps:**
1. Install signed MSIX package
2. Check app properties

**Expected Result:**
- [ ] Publisher name displayed correctly
- [ ] No "Unknown publisher" warnings
- [ ] Signature valid

---

### Test S.2: File Permissions

**Steps:**
1. Check app install location
2. Verify file permissions

**Expected Result:**
- [ ] App files have appropriate read/execute permissions
- [ ] No unnecessary write permissions
- [ ] AppData folder correctly used for user data

---

### Test S.3: Network Access

**Steps:**
1. Enable LLM post-processing with remote endpoint
2. Start dictation
3. Monitor network traffic

**Expected Result:**
- [ ] Only authorized network requests made
- [ ] No unexpected connections
- [ ] User can disable internet access (app still works for local-only features)

---

## Accessibility Tests

### Test A.1: Keyboard Navigation

**Steps:**
1. Launch application
2. Navigate UI using Tab, arrow keys
3. Access settings tabs via keyboard

**Expected Result:**
- [ ] All UI elements accessible via keyboard
- [ ] Tab order is logical
- [ ] No keyboard traps

---

### Test A.2: Screen Reader Compatibility

**Steps:**
1. Enable Windows Narrator or other screen reader
2. Navigate application
3. Test dictation features

**Expected Result:**
- [ ] UI elements announced correctly
- [ ] Buttons and controls have accessible names
- [ ] Status updates announced

---

## Cross-Platform Tests

### Test X.1: Windows 10 Compatibility

**Steps:**
1. Install on Windows 10 (build 22621 or later)
2. Run all core tests

**Expected Result:**
- [ ] All features work on Windows 10
- [ ] No crashes or compatibility issues

---

### Test X.2: Windows 11 Compatibility

**Steps:**
1. Install on Windows 11
2. Run all core tests

**Expected Result:**
- [ ] All features work on Windows 11
- [ ] Modern UI elements render correctly
- [ ] No issues with rounded corners or new visual effects

---

## Test Execution Summary

Use this checklist to track overall completion:

**Phase 6.1: System Tray Integration**
- [ ] 6.1.1-6.1.8 complete

**Phase 6.2: Auto-Start at Login**
- [ ] 6.2.1-6.2.5 complete

**Phase 6.3: MSIX Packaging**
- [ ] 6.3.1-6.3.8 complete

**Phase 6.4: Integration Tests**
- [ ] 6.4.1-6.4.5 complete

**Regression Tests (Phases 1-5)**
- [ ] R.1-R.6 complete

**Performance Tests**
- [ ] P.1-P.4 complete

**Security Tests**
- [ ] S.1-S.3 complete

**Accessibility Tests**
- [ ] A.1-A.2 complete

**Cross-Platform Tests**
- [ ] X.1-X.2 complete

---

**Total Tests:** 46
**Required for Phase 6 Sign-Off:** All Phase 6 tests (6.1-6.4) + all regression tests (R.1-R.6)

**Date:** ___________
**Tester:** ___________
**Build Version:** ___________
**Windows Version:** ___________
**Result:** [ ] PASS [ ] FAIL with notes attached