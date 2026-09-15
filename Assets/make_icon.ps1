$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# Builds gm_logo.png (transparent home-screen logo) and app.ico from gm_logo_raw.png.
# The GM artwork is red-on-white, so the white background is keyed out to alpha here —
# the app draws it on the near-black brand background. Re-run after replacing the raw file:
#   powershell -ExecutionPolicy Bypass -File Assets\make_icon.ps1

$assets = Split-Path -Parent $MyInvocation.MyCommand.Path
$rawPath = "$assets\gm_logo_raw.png"
if (-not (Test-Path $rawPath)) { throw "missing $rawPath" }
$src = [System.Drawing.Bitmap]::FromFile($rawPath)

# --- white -> alpha key, so the mark sits on the dark UI without a white card ---
# alpha = 255 - min(r,g,b): pure white vanishes, saturated red stays opaque, and the
# grey drop shadow survives as semi-transparent black.
$raw = New-Object System.Drawing.Bitmap($src.Width, $src.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$rectSrc = New-Object System.Drawing.Rectangle(0, 0, $src.Width, $src.Height)
$srcData = $src.LockBits($rectSrc, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$dstData = $raw.LockBits($rectSrc, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$rowLen = $src.Width * 4
$row = New-Object byte[] $rowLen
for ($y = 0; $y -lt $src.Height; $y++) {
    [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($srcData.Scan0, $y * $srcData.Stride), $row, 0, $rowLen)
    for ($i = 0; $i -lt $rowLen; $i += 4) {
        $b = $row[$i]; $g = $row[$i + 1]; $r = $row[$i + 2]; $a = $row[$i + 3]
        if ($a -eq 0) { continue }                      # already transparent, leave it
        $k = [Math]::Min($b, [Math]::Min($g, $r))       # how much white is in the pixel
        $na = 255 - $k
        if ($na -le 0) { $row[$i] = 0; $row[$i + 1] = 0; $row[$i + 2] = 0; $row[$i + 3] = 0; continue }
        $row[$i] = [byte][Math]::Min(255, [int](($b - $k) * 255 / $na))
        $row[$i + 1] = [byte][Math]::Min(255, [int](($g - $k) * 255 / $na))
        $row[$i + 2] = [byte][Math]::Min(255, [int](($r - $k) * 255 / $na))
        $row[$i + 3] = [byte][int]($na * $a / 255)
    }
    [System.Runtime.InteropServices.Marshal]::Copy($row, 0, [IntPtr]::Add($dstData.Scan0, $y * $dstData.Stride), $rowLen)
}
$src.UnlockBits($srcData); $raw.UnlockBits($dstData); $src.Dispose()

# --- find content bounding box on a small thumbnail ---
$tw = 200
$thumb = New-Object System.Drawing.Bitmap($raw, $tw, $tw)
$minX = $tw; $minY = $tw; $maxX = 0; $maxY = 0
for ($y = 0; $y -lt $tw; $y++) {
    for ($x = 0; $x -lt $tw; $x++) {
        if ($thumb.GetPixel($x, $y).A -gt 24) {
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$thumb.Dispose()
if ($maxX -lt $minX) { throw "gm_logo_raw.png looks empty after the white key" }
$scale = $raw.Width / $tw
$pad = [int](($maxX - $minX) * $scale * 0.10)
$bx = [Math]::Max(0, [int]($minX * $scale) - $pad)
$by = [Math]::Max(0, [int]($minY * $scale) - $pad)
$bw = [Math]::Min($raw.Width - $bx, [int](($maxX - $minX + 1) * $scale) + 2 * $pad)
$bh = [Math]::Min($raw.Height - $by, [int](($maxY - $minY + 1) * $scale) + 2 * $pad)
# square crop centered on the content (the GM mark is wide, so the square is driven by width)
$side = [Math]::Max($bw, $bh)
$cx = $bx + [int]($bw / 2); $cy = $by + [int]($bh / 2)
$sx = [Math]::Max(0, [Math]::Min($raw.Width - $side, $cx - [int]($side / 2)))
$sy = [Math]::Max(0, [Math]::Min($raw.Height - $side, $cy - [int]($side / 2)))
"content box: $bx,$by ${bw}x$bh -> square $sx,$sy side $side"

function CropScaled([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($raw, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)), $sx, $sy, $side, $side, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $bmp
}

# --- home-screen logo: transparent square crop, 512px ---
$homeLogo = CropScaled 512
$homeLogo.Save("$assets\gm_logo.png", [System.Drawing.Imaging.ImageFormat]::Png)
$homeLogo.Dispose()
"wrote gm_logo.png"

# --- icon master: black rounded tile + red border + logo, 256px ---
function MakeTile([int]$size) {
    $tile = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($tile)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    $r = [int]($size * 0.22)
    $d = $r * 2
    $edge = $size - 3
    $rect = New-Object System.Drawing.Rectangle(1, 1, $edge, $edge)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Black)
    $g.FillPath($bg, $path)

    $inset = [int]($size * 0.08)
    $logo = CropScaled ($size - 2 * $inset)
    $clip = $g.Clip
    $g.SetClip($path)
    $g.DrawImage($logo, $inset, $inset)
    $g.Clip = $clip
    $logo.Dispose()

    # brand red outline (#FF0000)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(170, 255, 0, 0), ([Math]::Max(1.0, $size / 96.0)))
    $g.DrawPath($pen, $path)
    $pen.Dispose(); $bg.Dispose(); $path.Dispose(); $g.Dispose()
    return $tile
}

# --- pack multi-size ICO: BMP entries for small sizes (max compatibility), PNG for 256 ---
function BmpEntry([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $rectAll = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $data = $bmp.LockBits($rectAll, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rowLen = $s * 4
    $xor = New-Object byte[] ($rowLen * $s)
    for ($y = 0; $y -lt $s; $y++) {
        $srcRow = [IntPtr]::Add($data.Scan0, $y * $data.Stride)
        $dstOff = ($s - 1 - $y) * $rowLen  # bottom-up
        [System.Runtime.InteropServices.Marshal]::Copy($srcRow, $xor, $dstOff, $rowLen)
    }
    $bmp.UnlockBits($data)

    $maskRow = [int][Math]::Ceiling($s / 8.0)
    $maskRow = ($maskRow + 3) -band (-bnot 3)  # pad to 4 bytes
    $and = New-Object byte[] ($maskRow * $s)   # all zero = rely on alpha channel

    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $w.Write([uint32]40); $w.Write([int]$s); $w.Write([int]($s * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]$xor.Length); $w.Write([int]0); $w.Write([int]0)
    $w.Write([uint32]0); $w.Write([uint32]0)
    $w.Write($xor); $w.Write($and)
    $bytes = $ms.ToArray()
    $w.Dispose(); $ms.Dispose()
    return , $bytes
}

$sizes = @(256, 64, 48, 32, 24, 16)
$blobs = @()
foreach ($s in $sizes) {
    $t = MakeTile $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $t.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $blobs += , @($s, $ms.ToArray())
        $ms.Dispose()
    }
    else {
        $blobs += , @($s, (BmpEntry $t))
    }
    $t.Dispose()
}

$icoPath = "$assets\app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bwr = New-Object System.IO.BinaryWriter($fs)
$bwr.Write([uint16]0); $bwr.Write([uint16]1); $bwr.Write([uint16]$blobs.Count)
$offset = 6 + 16 * $blobs.Count
foreach ($b in $blobs) {
    $s = $b[0]; $data = $b[1]
    $bwr.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # width
    $bwr.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $bwr.Write([byte]0); $bwr.Write([byte]0)                  # colors, reserved
    $bwr.Write([uint16]1); $bwr.Write([uint16]32)             # planes, bpp
    $bwr.Write([uint32]$data.Length); $bwr.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($b in $blobs) { $bwr.Write([byte[]]$b[1]) }
$bwr.Dispose(); $fs.Dispose()
$raw.Dispose()
"wrote app.ico ($((Get-Item $icoPath).Length) bytes, $($blobs.Count) sizes)"

# copy assets for the Avalonia project
$avAssets = Join-Path (Split-Path $assets -Parent) "TexRay/Assets"
New-Item -ItemType Directory -Force $avAssets | Out-Null
Copy-Item "$assets\app.ico", "$assets\gm_logo.png" $avAssets -Force
"copied to TexRay/Assets"
