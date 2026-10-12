<#
.SYNOPSIS
    Regenerates src/BtVolumeGuard/BtVolumeGuard.ico, the executable's icon.

.DESCRIPTION
    Draws the icon with the app's own TrayIconRenderer (dark headset glyph with a white outline and
    a green shield, visible on light and dark backgrounds) at the standard Windows icon sizes and
    writes them as PNG entries of one .ico file. Run it after changing TrayIconRenderer.

    Requires PowerShell 7 on .NET 10 or later (it loads the app's assembly) and a built Debug output.
#>
[CmdletBinding()]
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\src\BtVolumeGuard\BtVolumeGuard.ico')
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

dotnet build (Join-Path $root 'src\BtVolumeGuard') -c Debug --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Add-Type -AssemblyName System.Drawing
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $root 'src\BtVolumeGuard\bin\Debug\net10.0-windows\BtVolumeGuard.dll'))
$renderer = $assembly.GetType('BtVolumeGuard.TrayIconRenderer')
$style = [Enum]::Parse($assembly.GetType('BtVolumeGuard.GlyphStyle'), 'DarkWithHalo')
$render = $renderer.GetMethod('Render', [Reflection.BindingFlags]'Static,Public,NonPublic')

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($size in $sizes) {
    $bitmap = $render.Invoke($null, @($size, $style, $true))
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}

# ICO layout: ICONDIR (6 bytes), one ICONDIRENTRY (16 bytes) per image, then the PNG data.
$file = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Output), $file.ToArray())
"Wrote $([IO.Path]::GetFullPath($Output)) ($($sizes -join ', ') px)."
