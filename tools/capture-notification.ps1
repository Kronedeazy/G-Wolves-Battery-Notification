# Shows one notification type and saves a screenshot of the bottom-right screen corner.
# Usage: powershell -File tools\capture-notification.ps1 -Type start -Out docs\raw\start.png [-German]
# Build publish\MouseBattery.exe first (dotnet publish -c Release -o publish).
param([string]$Type, [string]$Out, [int]$Width = 1000, [int]$Height = 900, [switch]$German)
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -Namespace Dpi -Name Native -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[Dpi.Native]::SetProcessDPIAware() | Out-Null

$exe = Join-Path $PSScriptRoot '..\publish\MouseBattery.exe'
if ($German) { & $exe --show $Type --de } else { & $exe --show $Type }
Start-Sleep -Milliseconds 1800   # let the slide-in animation finish

$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$x = $wa.Right - $Width; $y = $wa.Bottom - $Height
$bmp = New-Object System.Drawing.Bitmap($Width, $Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
$g.Dispose()
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
