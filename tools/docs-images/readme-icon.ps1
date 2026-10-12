param([string]$Out, [string]$Glyph)
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'icon-layer.ps1')
(New-IconLayer 256 $Glyph).Save($Out, [Drawing.Imaging.ImageFormat]::Png)
