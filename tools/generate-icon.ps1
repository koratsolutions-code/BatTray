<#
.SYNOPSIS
    Generates src/BatTray.App/Assets/BatTray.ico (16–256 px, PNG-compressed entries) and a 512 px PNG.

.DESCRIPTION
    BatTray brand mark: a dark rounded square with a green battery outline and a charging bolt.
    Run from the repository root:  pwsh tools/generate-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\BatTray.App\Assets')
)

Add-Type -AssemblyName System.Drawing

$background = [System.Drawing.Color]::FromArgb(255, 0x1F, 0x29, 0x33)
$green = [System.Drawing.Color]::FromArgb(255, 0x3F, 0xB9, 0x50)

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Everything is drawn on a 256-unit grid and scaled.
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)

    # Rounded square background (small sizes use the full canvas for legibility).
    $inset = if ($size -le 24) { 0 } else { 8 }
    $bg = New-RoundedRectPath $inset $inset (256 - 2 * $inset) (256 - 2 * $inset) 56
    $g.FillPath((New-Object System.Drawing.SolidBrush $background), $bg)

    # Battery outline + terminal.
    $stroke = if ($size -le 24) { 20 } else { 14 }
    $pen = New-Object System.Drawing.Pen $green, $stroke
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $body = New-RoundedRectPath 44 80 152 96 18
    $g.DrawPath($pen, $body)
    $g.FillPath((New-Object System.Drawing.SolidBrush $green), (New-RoundedRectPath 202 108 18 40 6))

    # Charging bolt inside the battery.
    $bolt = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bolt.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(132, 92),
        [System.Drawing.PointF]::new(96, 134),
        [System.Drawing.PointF]::new(120, 134),
        [System.Drawing.PointF]::new(108, 166),
        [System.Drawing.PointF]::new(146, 120),
        [System.Drawing.PointF]::new(122, 120)))
    $g.FillPath((New-Object System.Drawing.SolidBrush $green), $bolt)

    $g.Dispose()
    return $bitmap
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
}

# ICO container: ICONDIR + one ICONDIRENTRY per image, followed by the PNG payloads.
$icoPath = Join-Path $OutputDirectory 'BatTray.ico'
$fs = [System.IO.File]::Create($icoPath)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$img.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write($img.Bytes) }
$w.Dispose()

$png = New-IconBitmap 512
$png.Save((Join-Path $OutputDirectory 'BatTray-512.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()

Write-Host "Wrote $icoPath and BatTray-512.png"
