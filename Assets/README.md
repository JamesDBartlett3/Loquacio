# MSIX Package Assets

This directory contains the image assets required for MSIX packaging.

## Required Images

All images should be PNG format. Create these using your preferred graphics tool (Paint.NET, Photoshop, GIMP, etc.).

| File | Size | Purpose |
|------|------|---------|
| StoreLogo.png | 50x50 px | Store listing logo |
| SmallTile.png | 71x71 px | Small tile display |
| Square150x150Logo.png | 150x150 px | Medium tile |
| Square44x44Logo.png | 44x44 px | App list icon |
| Wide310x150Logo.png | 310x150 px | Wide tile |
| LargeTile.png | 310x310 px | Large tile |
| SplashScreen.png | 620x300 px | App launch splash screen |

## Recommended Design

- Use a microphone icon or speech wave graphic
- Primary color: #0078D4 (Windows blue) or similar
- Transparent background where possible
- Clean, minimalist design
- Consistent style across all sizes

## Creating These Assets

1. Create a 620x300 px design first (highest resolution)
2. Scale down to other sizes
3. Ensure smallest sizes remain readable (44x44 and 50x50)
4. Use PNG format with transparency

## Windows App Studio (Alternative)

Alternatively, generate default assets using Microsoft's tools:
- [Windows App Studio](https://appstudio.microsoft.com/)
- Or use Visual Studio's Package.appxmanifest designer to auto-generate placeholder icons

## Notes

- These assets are referenced in both `Package.appxmanifest` and `WhisperDictation.Package.wapproj`
- Assets must be present before building the MSIX package
- Test the MSIX installation with placeholder icons first, then replace with final graphics