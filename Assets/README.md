# Package & Icon Assets

This directory contains the image assets required for MSIX packaging, plus the
canonical app logo.

## Logo

- `loquacio-logo.svg` — the canonical Loquacio swirl mark (vector). All icons
  in the repo (tray `.ico` files, `app_icon.ico`, flatpak SVG, macOS `.icns`)
  are generated from this file.
- `app_icon.ico` — multi-size Windows application icon (16–256 px), referenced
  by `ApplicationIcon` in `Loquacio/Loquacio.csproj`.
- `hero-banner.png` — README hero image containing the same mark.

## MSIX Tile Assets

All tile/splash images are PNG, generated from `loquacio-logo.svg`
(transparent background; `SplashScreen.png` uses the brand navy).

| File | Size | Purpose |
|------|------|---------|
| StoreLogo.png | 50x50 px | Store listing logo |
| SmallTile.png | 71x71 px | Small tile display |
| Square150x150Logo.png | 150x150 px | Medium tile |
| Square44x44Logo.png | 44x44 px | App list icon |
| Wide310x150Logo.png | 310x150 px | Wide tile |
| LargeTile.png | 310x310 px | Large tile |
| SplashScreen.png | 620x300 px | App launch splash screen |

## Regenerating

The icons and tiles are rendered from the SVG with `cairosvg` + Pillow. If
`loquacio-logo.svg` changes, regenerate:

1. Render the SVG to PNG at each required size.
2. Rebuild the multi-frame `.ico` files (sizes 16/24/32/48/256).
3. Re-emit the tile PNGs and splash screen.

These assets are referenced in both `Package.appxmanifest` and
`Loquacio.Package.wapproj`.
