# Images for the README and GitHub

PowerShell 7 scripts that drew the images in `docs/`. They load the Debug build of the app
(`src/BtVolumeGuard/bin/Debug/net10.0-windows/BtVolumeGuard.dll`), so run `dotnet build src` first.

| Script | Output |
|---|---|
| `menu-screenshot.ps1 -Dll <dll> -OutDir docs/screenshots [-Light]` | `tray-menu.png` / `tray-menu-light.png`: the tray menu drawn by WinForms with the same items as the app (texts are hard-coded in the script; keep them in sync with `TrayApplicationContext`) |
| `readme-icon.ps1 -Out <png> -Glyph '#F0F6FC'` (dark) / `'#1F2328'` (light) | `docs/icon-dark.png`, `docs/icon-light.png` |
| `social-preview.ps1 -Dll <dll> -Out docs/social-preview.png [-Light]` | GitHub social preview (upload manually: Settings → Social preview) |

`icon-layer.ps1` is a helper: the headset glyph inset from the edges plus the green shield.
Note: PowerShell variable names are case-insensitive; do not name a variable like a parameter (`$light` vs `-Light`).
