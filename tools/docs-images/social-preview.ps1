param([string]$Dll, [string]$Out, [switch]$Light)
Add-Type -AssemblyName System.Drawing
$asm = [Reflection.Assembly]::LoadFrom($Dll)
$render = $asm.GetType('BtVolumeGuard.TrayIconRenderer').GetMethod('Render', [Reflection.BindingFlags]'Static,Public,NonPublic')

$W = 1280; $H = 640
$bmp = [Drawing.Bitmap]::new($W, $H)
$g = [Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'
function C($hex) { [Drawing.ColorTranslator]::FromHtml($hex) }
$Pal = if ($Light) { @{ bg1 = '#FFFFFF'; bg2 = '#EEF2F6'; glow = 45; title = '#1F2328'; sub = '#3D444D'; small = '#59636E'; pill = '#15803D'; pillFill = 30; glyph = '#1B1B1B' } } else { @{ bg1 = '#0D1117'; bg2 = '#161B22'; glow = 70; title = '#F0F6FC'; sub = '#C9D1D9'; small = '#8B949E'; pill = '#4ADE80'; pillFill = 40; glyph = '#FFFFFF' } }

# Background: dark diagonal gradient with a soft green glow behind the icon.
$bg = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(0, 0), [Drawing.Point]::new($W, $H), (C $Pal.bg1), (C $Pal.bg2))
$g.FillRectangle($bg, 0, 0, $W, $H)
$glow = [Drawing.Drawing2D.GraphicsPath]::new(); $glow.AddEllipse(60, 120, 400, 400)
$pgb = [Drawing.Drawing2D.PathGradientBrush]::new($glow)
$pgb.CenterColor = [Drawing.Color]::FromArgb($Pal.glow, (C '#16A34A')); $pgb.SurroundColors = @([Drawing.Color]::FromArgb(0, (C '#16A34A')))
$g.FillPath($pgb, $glow)

# Icon, drawn by the app's own renderer at full size.
. (Join-Path $PSScriptRoot "icon-layer.ps1"); $icon = New-IconLayer 300 $Pal.glyph
$g.DrawImage($icon, 110, 170, 300, 300)

# Text.
$x = 480
$title = [Drawing.Font]::new('Segoe UI Semibold', 76, [Drawing.GraphicsUnit]::Pixel)
$sub = [Drawing.Font]::new('Segoe UI', 34, [Drawing.GraphicsUnit]::Pixel)
$small = [Drawing.Font]::new('Segoe UI', 26, [Drawing.GraphicsUnit]::Pixel)
$pill = [Drawing.Font]::new('Segoe UI Semibold', 30, [Drawing.GraphicsUnit]::Pixel)
$g.DrawString('BtVolumeGuard', $title, [Drawing.SolidBrush]::new((C $Pal.title)), $x - 6, 150)
$fmt = [Drawing.StringFormat]::new()
$g.DrawString("Stops Bluetooth headsets from jumping to 100% volume when a call starts.", $sub, [Drawing.SolidBrush]::new((C $Pal.sub)), [Drawing.RectangleF]::new($x, 262, 720, 100), $fmt)

# "100% -> 36%" pill.
$px = $x; $py = 392; $pw = 250; $ph = 56; $r = 28
$p = [Drawing.Drawing2D.GraphicsPath]::new()
$p.AddArc($px, $py, 2*$r, 2*$r, 90, 180); $p.AddArc($px + $pw - 2*$r, $py, 2*$r, 2*$r, 270, 180); $p.CloseFigure()
$g.FillPath([Drawing.SolidBrush]::new([Drawing.Color]::FromArgb($Pal.pillFill, (C '#16A34A'))), $p)
$g.DrawPath([Drawing.Pen]::new((C '#16A34A'), 2), $p)
$center = [Drawing.StringFormat]::new(); $center.Alignment = 'Center'; $center.LineAlignment = 'Center'
$g.DrawString("100%  →  36%", $pill, [Drawing.SolidBrush]::new((C $Pal.pill)), [Drawing.RectangleF]::new($px, $py, $pw, $ph), $center)

$g.DrawString('Windows 10 / 11  ·  tray app  ·  .NET 10  ·  MIT', $small, [Drawing.SolidBrush]::new((C $Pal.small)), $x, 488)

$bmp.Save($Out, [Drawing.Imaging.ImageFormat]::Png)
