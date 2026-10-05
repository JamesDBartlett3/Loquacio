# Homebrew Cask Formula for Whisper Dictation
# This file should be submitted to homebrew/cask or maintained in a custom tap
#
# Installation:
#   brew install --cask whisper-dictation
#
# Or from a custom tap:
#   brew tap jamesdbartlett3/cask
#   brew install --cask whisper-dictation

cask "whisper-dictation" do
  version "0.0.0.git-PLACEHOLDER"
  sha256 :no_check

  url "https://github.com/JamesDBartlett3/whisper-dictation/releases/download/v#{version}/whisper-dictation-macos.zip"
  name "Whisper Dictation"
  desc "Local Whisper-powered voice-to-text dictation"
  homepage "https://github.com/JamesDBartlett3/whisper-dictation"

  app "WhisperDictation.app"

  uninstall launchctl: "com.github.jamesdbartlett3.whisper-dictation.daemon",
            quit:      "com.github.jamesdbartlett3.whisper-dictation"

  zap trash: [
    "~/Library/Application Support/WhisperDictation",
    "~/Library/Caches/WhisperDictation",
    "~/Library/Logs/WhisperDictation",
    "~/Library/Preferences/com.github.jamesdbartlett3.whisper-dictation.plist",
  ]
end
