---
title: My Coworkers Pay Monthly to Stream Their Voice to a Datacenter. So I Built Them Loquacio — a Free Dictation App That Never Phones Home
published:
tags: devchallenge, weekendchallenge, hf26challenge
---

*This is a submission for the [Hacktoberfest Weekend Challenge: Build for a Friend](https://dev.to/challenges/hacktoberfest-weekend-2026-10-01)*

<!-- Before publishing: add the demo video in the Demo section. -->

## What I Built

This one's for my coworkers at P3 Adaptive. Many of them dictate every day, and many of them pay a monthly subscription for commercial apps that don't even process the audio locally — so there's always a "who's potentially reading this?" question looming over everything. Their voice, recorded at their own desk, streamed to somebody else's datacenter. Some of them live with it; the rest just kept typing.

So I built them **Loquacio**: a desktop dictation app where every millisecond of audio stays on the machine it was spoken into, and which costs exactly nothing. That last part matters: since it's built entirely from open-source components — Whisper, local LLM servers, .NET itself — the subscription apps they've been paying for really ought to be free. Loquacio is me making that true. Speech recognition runs on a local Whisper model through [Whisper.net](https://github.com/sandrohanea/whisper.net). If they want punctuation and filler-word cleanup, that goes through their *own* local LLM — LM Studio or Ollama on localhost. The only network connection the app ever needs is to download the Whisper model files the first time. After that, it works on a plane.

It's not a toy voice-note recorder — it's a real dictation workflow:

- **Continuous mode** listens and transcribes as you speak, chunking on silence
- **Push-to-talk** holds a hotkey for precise, on-demand capture
- **Keyword activation** toggles listening with a spoken wake word
- **Custom vocabulary** teaches it the names and jargon Whisper would otherwise mangle
- **Local LLM post-processing** fixes capitalization, punctuation, "um"s, and misheard words — offline

The architecture is what makes it pleasant to live with: a small headless **daemon** owns the microphone and the model, and any controller — a full Avalonia GUI, a keyboard-only TUI, or the Windows WPF app — attaches to it over a private socket. Start the daemon once; dictate from whichever surface you're in.

## Demo

<!-- Record a short clip: hotkey press → speak → text lands in an editor.
     Embed the video here, or drop 2–3 screenshots of the Avalonia GUI and TUI. -->

<!-- 🎬 Video demo: *[add before publishing]* -->

The app itself ships as self-contained desktop builds (no runtime installation needed):

| Platform | Build |
|----------|-------|
| Linux | `.AppImage`, `.deb` (with a systemd user service), portable `.tar.gz` |
| Windows 10/11 | portable `.zip` with daemon auto-start script; MSIX buildable on a Windows host |

Releases: [v0.1.0 on GitHub](https://github.com/JamesDBartlett3/Loquacio/releases)

## Code

The full source is on GitHub: [JamesDBartlett3/Loquacio](https://github.com/JamesDBartlett3/Loquacio)

```bash
git clone https://github.com/JamesDBartlett3/Loquacio.git
dotnet build -c Release
dotnet test Loquacio.Tests -c Release   # 373 tests, all platforms
```

.NET 10, C#, OSL 3.0 licensed. The test suite runs on Linux and Windows alike — including the daemon, IPC, and transcription pipeline.

## How I Built It

The core AI is **Whisper**, OpenAI's open-source speech recognition model, running *inference locally* via Whisper.net — no API, no account, no per-minute billing. The app downloads whichever open-weight model size fits the user's hardware (tiny through large-v3) and validates checksums, so a corrupted model can't silently produce garbage.

The post-processing layer leans on the open **OpenAI-compatible API shape** that LM Studio and Ollama expose locally: the same protocol the cloud services use, pointed at `localhost`. That means the "smart cleanup" pass is swappable — any model they run locally works, and nothing is locked to one vendor.

On the engineering side, the interesting decisions:

- **Daemon-first architecture.** Audio capture and inference live in one long-running process; GUIs are thin clients. The dictation state machine (sessions, stage timeouts) means a controller crash never interrupts a transcription.
- **Cross-platform by construction.** The core library is plain .NET; platform code (WASAPI on Windows, D-Bus desktop integration on Linux) is isolated at the edges. The whole test suite runs on Linux even though one controller is WPF.
- **Honest packaging.** Linux builds are cross-published on Linux (AppImage validated without FUSE, deb, tarball); the Windows zip is cross-built with `EnableWindowsTargeting` and every `.exe` verified as a real PE32+ binary before shipping. v0.1.0 went out this weekend with all four artifacts attached.

## Why Does Open Innovation Matter?

Because voice is the most sensitive data type most people ever produce, and the default options all route it through someone's cloud. Open-source AI at the core changes that equation completely:

- **Privacy isn't a policy, it's a property.** There is no server to trust, no retention policy to read, no training opt-out to hunt for. The audio physically cannot leave the machine.
- **It works offline, forever.** No subscription, no rate limit, no deprecation. The app my coworkers run today will work identically in ten years.
- **The model is a setting, not a lock-in.** Whisper model sizes are a dropdown. The LLM cleanup layer speaks a standard local API. When a better open model ships next year, it's a swap, not a migration.
- **Zero marginal cost.** My coworkers can dictate all day without anyone's meter running.

## My Agent Session

This project was built pair-programming style with local AI agents. The agents planned phases, wrote and reviewed code through a real PR process (every change merged via review, including a dependency CVE adjudication), ran the 373-test suite, and built the release artifacts — while the human steered. Fitting, for an app about talking to your computer instead of typing at it.

## Prize Categories

- Overall
- Featured: Best use of open-source AI / local inference
