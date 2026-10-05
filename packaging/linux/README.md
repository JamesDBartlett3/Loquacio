# Linux Packaging (Phase D)

Builds self-contained (no .NET runtime required) Linux packages for the
daemon + TUI + Avalonia controllers.

## Outputs

| Artifact | Path | Contents |
|---|---|---|
| Portable tarball | `dist/loquacio-<ver>-linux-x64.tar.gz` | daemon + tui + avalonia + README |
| Debian package | `dist/loquacio_<ver>_amd64.deb` | installs to `/usr/lib/loquacio`, symlinks in `/usr/bin`, systemd **user** service |

## Build (from repo root; host needs `dotnet` SDK 10 + `dpkg-deb`, or docker)

```bash
packaging/linux/build-packages.sh            # publish + package
packaging/linux/build-packages.sh --build-only   # publish only (already inside a container / native dotnet)
```

Publishing uses a local `dotnet` SDK 10 if available; otherwise it falls back
to the official `mcr.microsoft.com/dotnet/sdk:10.0` image via docker.
`dpkg-deb` packaging always runs on the host — any Debian/Ubuntu host has it.

## Install (deb)

```bash
sudo dpkg -i dist/loquacio_*_amd64.deb
systemctl --user enable --now loquacio-daemon.service
/usr/bin/loquacio-tui        # TUI controller
/usr/bin/loquacio-avalonia   # GUI controller
```

The daemon is a **user** service (no root daemon; matches PipeWire's
per-user audio model). It is *not* auto-enabled by the package — the user
opts in with `systemctl --user enable`.

## Future work (not in this slice)

- .AppImage + Flatpak wrappers (the tarball layout is AppImage-ready:
  single `loquacio/` prefix, relative symlinks)
- macOS: .app bundle, launchd agent, CGEvent injection (separate task)
- Auto-update mechanism (task 3 of #164)
