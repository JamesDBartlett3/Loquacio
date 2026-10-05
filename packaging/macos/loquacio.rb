# Homebrew Cask Formula for Loquacio
# This file should be submitted to homebrew/cask or maintained in a custom tap
#
# Installation:
#   brew install --cask loquacio
#
# Or from a custom tap:
#   brew tap jamesdbartlett3/cask
#   brew install --cask loquacio

cask "loquacio" do
  version "0.0.0.git-PLACEHOLDER"
  sha256 :no_check

  url "https://github.com/JamesDBartlett3/loquacio/releases/download/v#{version}/loquacio-macos.zip"
  name "Loquacio"
  desc "Local Whisper-powered voice-to-text dictation"
  homepage "https://github.com/JamesDBartlett3/loquacio"

  app "Loquacio.app"

  uninstall launchctl: "com.github.jamesdbartlett3.loquacio.daemon",
            quit:      "com.github.jamesdbartlett3.loquacio"

  zap trash: [
    "~/Library/Application Support/Loquacio",
    "~/Library/Caches/Loquacio",
    "~/Library/Logs/Loquacio",
    "~/Library/Preferences/com.github.jamesdbartlett3.loquacio.plist",
  ]
end
