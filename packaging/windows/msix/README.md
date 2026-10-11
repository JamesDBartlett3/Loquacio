# MSIX Packaging (Avalonia App) — Windows Host Required

MSIX packages **cannot be built on the Linux dev host** (no Windows SDK / MakeAppx).
Everything here is groundwork: manifest, assets, and a build script to run on Windows.

## Layout

- `AppxManifest.xml` — package manifest for the Avalonia app (`loquacio-avalonia.exe`, full-trust, desktop-only)
- `assets/` — placeholder tile/logo/splash PNGs generated from `Loquacio.Avalonia/Assets/loquacio-icon.png`
- `scripts/package-msix.ps1` (repo root `scripts/`) — end-to-end Windows build: publish → stage → MakeAppx → optional sign

## Build on Windows

Prerequisites: .NET 10 SDK, Windows 10/11 SDK (MakeAppx.exe + signtool.exe on PATH via Kits bin folder).

```powershell
# Unsigned package (for local testing with a self-signed cert installed separately)
.\scripts\package-msix.ps1 -Runtime win-x64

# Self-contained, versioned, signed
.\scripts\package-msix.ps1 -Runtime win-x64 -SelfContained -Version 1.2.0.0 -Sign -CertPath .\loquacio.pfx -CertPassword <pwd>
```

Output: `dist/msix/Loquacio-<version>-<runtime>.msix`

## Install for testing

```powershell
# Self-signed cert: create + trust once
New-SelfSignedCertificate -Type Custom -Subject "CN=Loquacio" -KeyUsage DigitalSignature -FriendlyName "Loquacio Dev" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.19={text}")
Export-Certificate -Cert (Get-ChildItem Cert:\CurrentUser\My | Where-Object FriendlyName -eq "Loquacio Dev") -FilePath .\loquacio.cer
Import-Certificate -FilePath .\loquacio.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
signtool sign /fd SHA256 /a /f .\loquacio.pfx dist\msix\Loquacio-1.0.0.0-win-x64.msix
Add-AppxPackage -Path dist\msix\Loquacio-1.0.0.0-win-x64.msix
```

## Notes

- `Identity/Publisher` must match the signing cert subject (`CN=Loquacio`).
- MSIX `StartupTask` replaces the HKCU Run-key auto-start used by unpackaged builds (Phase 6.3).
- The legacy WPF packaging (`Package.appxmanifest` + `Loquacio.Package.wapproj` at repo root) predates the Avalonia UI and is kept for reference; the Avalonia manifest here is the forward path.
- Placeholder assets should eventually be replaced with final branded art (including targetsize/contrast variants).

