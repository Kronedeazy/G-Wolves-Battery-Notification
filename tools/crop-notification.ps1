# Cuts the notification out of a corner screenshot and makes the rounded corners transparent.
# Finds the notification edges by walking along its padding until the color jumps (the background is a soft gradient).
param([string]$In, [string]$Out, [int]$Radius = 12, [int]$Tolerance = 25)
Add-Type -AssemblyName System.Drawing
$src = New-Object System.Drawing.Bitmap($In)

function Test-Near($a, $b) { ([Math]::Abs($a.R - $b.R) + [Math]::Abs($a.G - $b.G) + [Math]::Abs($a.B - $b.B)) -lt $Tolerance }

# A point in the bottom padding of the notification
$sx = $src.Width - 40; $sy = $src.Height - 25
$bg = $src.GetPixel($sx, $sy)

$bottom = $sy; while ($bottom -lt $src.Height - 1 -and (Test-Near $src.GetPixel($sx, $bottom + 1) $src.GetPixel($sx, $bottom))) { $bottom++ }
$row = $bottom - 4
$right = $sx; while ($right -lt $src.Width - 1 -and (Test-Near $src.GetPixel($right + 1, $row) $src.GetPixel($right, $row))) { $right++ }
$left = $sx;  while ($left -gt 0 -and (Test-Near $src.GetPixel($left - 1, $row) $src.GetPixel($left, $row))) { $left-- }
$col = $right - 6
$top = $sy;   while ($top -gt 0 -and (Test-Near $src.GetPixel($col, $top - 1) $src.GetPixel($col, $top))) { $top-- }

# include the 1-px border
$left = [Math]::Max(0, $left - 1); $top = [Math]::Max(0, $top - 1)
$right = [Math]::Min($src.Width - 1, $right + 1); $bottom = [Math]::Min($src.Height - 1, $bottom + 1)
$w = $right - $left + 1; $h = $bottom - $top + 1

$bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$d = $Radius * 2
$path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d - 1, 0, $d, $d, 270, 90)
$path.AddArc($w - $d - 1, $h - $d - 1, $d, $d, 0, 90); $path.AddArc(0, $h - $d - 1, $d, $d, 90, 90); $path.CloseFigure()
$g.SetClip($path)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $w, $h)), (New-Object System.Drawing.Rectangle($left, $top, $w, $h)), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"{0}: {1}x{2} at ({3},{4})" -f (Split-Path $Out -Leaf), $w, $h, $left, $top
