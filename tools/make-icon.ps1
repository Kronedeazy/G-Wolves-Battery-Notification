# Draws the app icon (mouse with battery bar) and writes assets\icon.png (256 px) and assets\icon.ico.
# Run with Windows PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # background tile
    $bg = RoundRect (8*$s) (8*$s) (240*$s) (240*$s) (52*$s)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 40, 52))), $bg)

    # mouse body
    $body = RoundRect (74*$s) (36*$s) (108*$s) (184*$s) (54*$s)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 236, 240, 245))), $body)

    # button split and wheel
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 32, 40, 52)), (6*$s)
    $g.DrawLine($pen, (128*$s), (36*$s), (128*$s), (96*$s))
    $wheel = RoundRect (120*$s) (56*$s) (16*$s) (30*$s) (8*$s)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 40, 52))), $wheel)

    # battery level bar
    $slot = RoundRect (100*$s) (116*$s) (56*$s) (80*$s) (10*$s)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 40, 52))), $slot)
    $fill = RoundRect (108*$s) (146*$s) (40*$s) (42*$s) (6*$s)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 46, 204, 113))), $fill)

    $g.Dispose()
    return $bmp
}

$png = Join-Path $assets 'icon.png'
$big = Draw 256
$big.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)

# .ico with PNG entries 16/24/32/48/64/256
$sizes = 16, 24, 32, 48, 64, 256
$images = foreach ($z in $sizes) {
    $b = Draw $z
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $z = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($z -ge 256) { 0 } else { $z }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'icon.ico'), $out.ToArray())
Write-Host "Icon written to $assets"
