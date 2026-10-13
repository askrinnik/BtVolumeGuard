param([string]$Dll, [string]$OutDir, [switch]$Light)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$asm = [Reflection.Assembly]::LoadFrom($Dll)
$render = $asm.GetType('BtVolumeGuard.TrayIconRenderer').GetMethod('Render', [Reflection.BindingFlags]'Static,Public,NonPublic')
$glyphStyle = [Enum]::Parse($asm.GetType('BtVolumeGuard.GlyphStyle'), $(if ($Light) { 'Dark' } else { 'Light' }))
$T = if ($Light) { @{ bg = '#F3F3F3'; bar = '#E8E8E8'; hover = [Drawing.Color]::FromArgb(25, 0, 0, 0); shadow = 35; suffix = '-light' } } else { @{ bg = '#2B2B2B'; bar = '#1C1C1C'; hover = [Drawing.Color]::FromArgb(40, 255, 255, 255); shadow = 60; suffix = '' } }
function Item($text, [bool]$checked = $false, [bool]$enabled = $true) {
  $i = [System.Windows.Forms.ToolStripMenuItem]::new($text); $i.Checked = $checked; $i.Enabled = $enabled; $i }
function Sep { [System.Windows.Forms.ToolStripSeparator]::new() }

function Capture-Menu($items) {
  $menu = [System.Windows.Forms.ContextMenuStrip]::new()
  foreach ($it in $items) { [void]$menu.Items.Add($it) }
  $menu.PerformLayout()
  $size = $menu.GetPreferredSize([Drawing.Size]::Empty); $menu.Size = $size
  $bmp = [Drawing.Bitmap]::new($size.Width, $size.Height)
  $menu.DrawToBitmap($bmp, [Drawing.Rectangle]::new(0, 0, $size.Width, $size.Height))
  $bmp
}

$devicesItem = Item 'Devices'
[void]$devicesItem.DropDownItems.Add((Item 'x'))
$logItem = Item 'Log'
[void]$logItem.DropDownItems.Add((Item 'Off'))
$main = Capture-Menu @(
  (Item 'Headphones (LK12): 36%' $false $false), (Sep),
  $devicesItem,
  (Item 'Guard enabled' $true), (Item 'Notify when a jump is reverted' $true), (Item 'Start with Windows' $true), (Sep),
  $logItem, (Item 'Open settings'), (Item 'Reload settings'), (Sep), (Item 'About BtVolumeGuard…'), (Item 'Exit'))
$sub = Capture-Menu @(
  (Item 'Headphones (G2) (offline)' $true), (Item 'Headphones (LK12)' $true), (Item 'Speakers (JBL Flip 4) (offline)'))

function Compose($menuBmp, $subBmp, $subTop, $out) {
  $pad = 16; $barH = 48; $gap = 8
  $W = $menuBmp.Width + $pad * 2 + $(if ($subBmp) { $subBmp.Width - 4 } else { 0 })
  $H = $menuBmp.Height + $gap + $barH + $pad
  $bmp = [Drawing.Bitmap]::new($W, $H); $g = [Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.ColorTranslator]::FromHtml($T.bg))
  # Taskbar strip with only the app's icon (Windows 11 tray hover highlight).
  $barY = $H - $barH
  $g.FillRectangle([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($T.bar)), 0, $barY, $W, $barH)
  $iconX = $pad + $menuBmp.Width - 44
  $g.FillRectangle([Drawing.SolidBrush]::new($T.hover), $iconX - 8, $barY + 8, 40, $barH - 16)
  $icon = $render.Invoke($null, @(24, $glyphStyle, $true))
  $g.DrawImage($icon, $iconX, $barY + ($barH - 24) / 2, 24, 24)
  # Menu (with a soft shadow), and the submenu to its right.
  $mx = $pad; $my = $pad
  $g.FillRectangle([Drawing.SolidBrush]::new([Drawing.Color]::FromArgb($T.shadow, 0, 0, 0)), $mx + 3, $my + 3, $menuBmp.Width, $menuBmp.Height)
  $g.DrawImage($menuBmp, $mx, $my)
  if ($subBmp) {
    $sx = $mx + $menuBmp.Width - 4; $sy = $my + $subTop
    $g.FillRectangle([Drawing.SolidBrush]::new([Drawing.Color]::FromArgb($T.shadow, 0, 0, 0)), $sx + 3, $sy + 3, $subBmp.Width, $subBmp.Height)
    $g.DrawImage($subBmp, $sx, $sy)
  }
  $bmp.Save($out, [Drawing.Imaging.ImageFormat]::Png)
}

Compose $main $sub ($devicesItem.Bounds.Y - 3) (Join-Path $OutDir "tray-menu$($T.suffix).png")
"main menu $($main.Width)x$($main.Height)"
