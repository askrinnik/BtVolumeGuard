# Draws the icon layer (white headset glyph inset from the edges + green shield badge) into a bitmap.
function New-IconLayer([int]$s, [string]$glyph = '#FFFFFF') {
  $layer = [Drawing.Bitmap]::new($s, $s)
  $lg = [Drawing.Graphics]::FromImage($layer); $lg.SmoothingMode = 'AntiAlias'
  $fmt = [Drawing.StringFormat]::new(); $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
  $path = [Drawing.Drawing2D.GraphicsPath]::new()
  $path.AddString([string][char]0xE95B, [Drawing.FontFamily]::new('Segoe Fluent Icons'), 0, $s * 0.84, [Drawing.RectangleF]::new($s * 0.03, $s * 0.07, $s * 0.84, $s * 0.84), $fmt)
  $lg.FillPath([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($glyph)), $path)
  function Shield([float]$x, [float]$y, [float]$w, [float]$h) {
    $p = [Drawing.Drawing2D.GraphicsPath]::new()
    $p.AddLine($x + $w/2, $y, $x + $w, $y + $h*0.18)
    $p.AddBezier($x + $w, $y + $h*0.18, $x + $w, $y + $h*0.65, $x + $w*0.75, $y + $h*0.85, $x + $w/2, $y + $h)
    $p.AddBezier($x + $w/2, $y + $h, $x + $w*0.25, $y + $h*0.85, $x, $y + $h*0.65, $x, $y + $h*0.18)
    $p.CloseFigure(); $p }
  $w = $s * 0.52; $h = $w * 1.1; $x = $s - $w; $y = $s - $h; $gap = $s * 0.06
  $lg.CompositingMode = 'SourceCopy'
  $lg.FillPath([Drawing.SolidBrush]::new([Drawing.Color]::Transparent), (Shield ($x - $gap) ($y - $gap) ($w + 2*$gap) ($h + 2*$gap)))
  $lg.CompositingMode = 'SourceOver'
  $lg.FillPath([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#16A34A')), (Shield $x $y $w $h))
  $layer
}
