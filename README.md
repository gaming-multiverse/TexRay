# TexRay - GamingMultiverse

**v1.0.0 · Developer: perseuslive_**

Windows GUI tool that checks custom FiveM peds from creators against the server's
compliance rules. Built on CodeWalker.Core — it opens every `.ydd` / `.ytd` (and,
with the "Include .yft / .ydr / .ycd" toggle on, every `.yft` / `.ydr` too) and reads
real texture formats, resolutions and geometry counts. `.ycd` / `.ymt` files are
counted toward the size limit when the toggle is on.

## The rules (all editable in the UI)

| Rule | Default |
|---|---|
| Max standalone `.ytd` files | **200** |
| Max total size of all scanned files | **200 MB** |
| Max vertices per geometry | **60,000** |
| Max polygons (triangles) per geometry | **60,000** |
| Max vertices / polygons per whole clothing item (sum of its parts) | **60,000** |
| Max texture resolution (embedded + standalone) | **2048×2048 (2K)** |
| Allowed texture compression formats | **DXT1, DXT5, BC7** |

Counts come from the **high-detail (LOD0) model only** — the Med/Low/VLow LOD copies a
drawable also stores are ignored, so the numbers match OpenIV / CodeWalker / Blender.
The clothing-item check is a normal checkbox in the scan options (on by default) —
untick it to check only individual parts.

Geometry and embedded-texture checks (and the texture auto-fix) apply equally to
`.ydd`, `.yft` (all fragment drawables: main, cloth, physics children) and `.ydr`.

Files that can't be parsed (corrupted / newer "Gen9" compression that CodeWalker.Core
1.0.3 can't read) are listed as violations too, never silently skipped.

## Run it

The app is the Avalonia project in `TexRay/` (UI in `*.axaml`, scan/fix/report
logic in `TexRay/Core/`):

```
cd TexRay
dotnet build -c Release
```

then double-click `TexRay/bin/Release/net8.0-windows/TexRay.exe`.

- **Home screen** — pick a check: CheckerYDD (vertices/polygons), Embedded Compression,
  Texture Compression, Texture Resolution Scanner, Resolution Scanner 2 (YTD), or
  **Full Scan** (everything at once, including the 200-YTD / 200-MB resource limits).
- **SEARCH** picks the ped folder (or paste a path). **SCAN** runs the checks; the
  results list only the files that break a rule — clean files never clutter the grid.
- The YTD-count and total-size limits are always evaluated and shown in the results
  summary, whatever the mode.
- **EXPORT HTML** writes the English compliance report (one table per violation category,
  plus the resource-limit summary) — ready to post on Discord. **EXPORT JSON** writes
  the raw extraction data. **COPY TEXT** puts a Discord-ready text version (same section
  titles, file lists in code blocks) straight on the clipboard.
- **Drag & drop** a ped folder anywhere onto the window — on the home screen it goes
  straight into Full Scan with that folder.
- The last folder and all thresholds are **remembered between launches**
  (`%AppData%/TexRay-GamingMultiverse/settings.json`).
- **FIX TEXTURES** repairs texture violations in place: oversized textures are downscaled
  to the limit (high-quality bicubic + regenerated mip chain) and disallowed formats are
  re-encoded to DXT1 (opaque) / DXT5 (alpha). Works on standalone `.ytd` and on textures
  embedded in `.ydd` (mesh data is untouched). Every original is kept next to the fixed
  file as `.bak`, a fixed file is only written after it successfully re-parses, and the
  tool re-scans automatically afterwards. Geometry violations (vertices/polygons) and
  unreadable files can NOT be auto-fixed — those need the creator to rework the model.

## Console mode (optional)

The same exe also runs headless from a terminal:

```
TexRay.exe "C:\peds\mz_maze" extract.json
```

Runs a full scan with the default limits and writes both `extract.json` and
`extract.html`. Exit code 0 = all clear, 2 = violations found. Add `--fix` to also
repair the texture violations (with `.bak` backups) and re-scan before reporting.

## Notes on interpretation

- **YTD count** counts standalone `.ytd` files only (embedded texture dictionaries inside
  `.ydd` files aren't separate stream files, so they don't count toward this limit).
- **Total size** sums every scanned `.ydd`/`.ytd` file's size on disk.
- Format comparison strips the `D3DFMT_` prefix (`D3DFMT_DXT5` → `DXT5`) before checking
  the whitelist, so BC7 and other modern formats compare correctly.
- The machine needs a .NET 8+ runtime; the app rolls forward to whatever major version
  is installed (this machine has .NET 10).
