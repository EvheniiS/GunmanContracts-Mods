$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = Join-Path $PSScriptRoot 'VirtualDesktop.Android-20261004-183202.jpg'
$overlayPath = Join-Path $PSScriptRoot 'MeleeUnlocks-Nexus-thumbnail-overlay.png'
$outputPath = Join-Path $PSScriptRoot 'MeleeUnlocks-Nexus-thumbnail-text-only.png'
$uploadPath = Join-Path $PSScriptRoot 'MeleeUnlocks-Nexus-thumbnail-1600x900.png'
$source = [System.Drawing.Bitmap]::new($sourcePath)
$overlay = [System.Drawing.Bitmap]::new($source.Width, $source.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($overlay)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$scale = $source.Width / 1672.0
$graphics.ScaleTransform($scale, $scale)

function Add-Text([string]$text, [string]$family, [float]$x, [float]$y, [float]$width, [float]$height, [string]$color, [bool]$italic = $false) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new([System.Drawing.Drawing2D.FillMode]::Winding)
    $fontFamily = [System.Drawing.FontFamily]::new($family)
    $style = [System.Drawing.FontStyle]::Regular
    if ($italic) { $style = [System.Drawing.FontStyle]::Italic }
    $path.AddString($text, $fontFamily, [int]$style, 100, [System.Drawing.PointF]::new(0, 0), [System.Drawing.StringFormat]::GenericTypographic)
    $bounds = $path.GetBounds()
    $matrix = [System.Drawing.Drawing2D.Matrix]::new()
    $matrix.Translate(-$bounds.X, -$bounds.Y)
    $path.Transform($matrix)
    $matrix.Reset()
    $matrix.Scale($width / $bounds.Width, $height / $bounds.Height)
    $path.Transform($matrix)
    $matrix.Reset()
    $matrix.Translate($x, $y)
    $path.Transform($matrix)

    $shadow = $path.Clone()
    $matrix.Reset()
    $matrix.Translate(7, 9)
    $shadow.Transform($matrix)
    foreach ($shadowWidth in @(24, 18, 12, 6)) {
        $softShadow = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(28, 0, 0, 0), $shadowWidth)
        $softShadow.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $graphics.DrawPath($softShadow, $shadow)
        $softShadow.Dispose()
    }
    $black = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(220, 0, 0, 0))
    $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(210, 0, 0, 0), 2)
    $outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $fill = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($color))
    $graphics.FillPath($black, $shadow)
    $graphics.DrawPath($outline, $path)
    $graphics.FillPath($fill, $path)

    if ($italic) {
        # Distress is clipped inside the letter shapes; the game image stays untouched.
        $savedState = $graphics.Save()
        $graphics.SetClip($path, [System.Drawing.Drawing2D.CombineMode]::Intersect)
        $random = [System.Random]::new(401 + [int]$x)
        for ($i = 0; $i -lt 260; $i++) {
            $px = $x + $random.NextDouble() * $width
            $py = $y + $random.NextDouble() * $height
            $crack = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb($random.Next(35, 110), 140, 71, 14), [single](0.3 + $random.NextDouble() * 0.8))
            $points = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new($px, $py))
            for ($segment = 0; $segment -lt $random.Next(2, 6); $segment++) {
                $px += $random.Next(-5, 15)
                $py -= $random.Next(3, 16)
                $points += [System.Drawing.PointF]::new($px, $py)
            }
            $graphics.DrawLines($crack, [System.Drawing.PointF[]]$points)
            $crack.Dispose()
        }
        for ($i = 0; $i -lt 1300; $i++) {
            $px = $x + $random.NextDouble() * $width
            $py = $y + $random.NextDouble() * $height
            $grain = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($random.Next(35, 135), 115, 58, 13))
            $size = [single](0.4 + [Math]::Pow($random.NextDouble(), 5) * 3)
            $graphics.FillEllipse($grain, [single]$px, [single]$py, $size, [single]($size * 1.6))
            $grain.Dispose()
        }
        $graphics.Restore($savedState)
    }
    $fill.Dispose(); $outline.Dispose(); $black.Dispose(); $shadow.Dispose(); $matrix.Dispose(); $path.Dispose(); $fontFamily.Dispose()
}

# Text occupies the dark ceiling and floor; every knife on the wall remains visible.
Add-Text 'G U N M A N   C O N T R A C T S' 'Bahnschrift Condensed' 65 23 550 29 '#FFF1D8'
Add-Text 'MELEE' 'Impact' 55 69 495 151 '#FFF1D8' $true
Add-Text 'UNLOCKS' 'Impact' 565 69 1010 151 '#FFB800' $true
$rule = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#FFB800'))
$graphics.FillRectangle($rule, 66, 813, 1000, 6)
$rule.Dispose()
Add-Text 'DOUBLE KATANA BY DEFAULT' 'Impact' 68 830 1100 43 '#FFF1D8'
Add-Text 'ALL KNIVES OPTIONAL' 'Impact' 68 882 860 43 '#FFF1D8'
$graphics.Dispose()
$overlay.Save($overlayPath, [System.Drawing.Imaging.ImageFormat]::Png)

$canvas = [System.Drawing.Bitmap]::new($source)
$compositor = [System.Drawing.Graphics]::FromImage($canvas)
$compositor.DrawImageUnscaled($overlay, 0, 0)
$compositor.Dispose()

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
public static class TextOverlayCheck {
    public static long Verify(Bitmap source, Bitmap overlay, Bitmap result) {
        if (source.Width != overlay.Width || source.Height != overlay.Height || source.Width != result.Width || source.Height != result.Height)
            throw new Exception("Image dimensions changed.");
        long untouched = 0;
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                if (overlay.GetPixel(x, y).A == 0) {
                    if (source.GetPixel(x, y).ToArgb() != result.GetPixel(x, y).ToArgb())
                        throw new Exception("Background pixel changed outside the text overlay at " + x + "," + y);
                    untouched++;
                }
        return untouched;
    }
}
'@ -ReferencedAssemblies System.Drawing

$untouched = [TextOverlayCheck]::Verify($source, $overlay, $canvas)
$canvas.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$upload = [System.Drawing.Bitmap]::new(1600, 900)
$exportGraphics = [System.Drawing.Graphics]::FromImage($upload)
$exportGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$exportGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$exportGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$exportGraphics.DrawImage($canvas, [System.Drawing.Rectangle]::new(0, 0, 1600, 900), [System.Drawing.Rectangle]::new(0, 0, $canvas.Width, $canvas.Height), [System.Drawing.GraphicsUnit]::Pixel)
$exportGraphics.Dispose()
$upload.Save($uploadPath, [System.Drawing.Imaging.ImageFormat]::Png)
$upload.Dispose()
Write-Output "Saved $outputPath ($($canvas.Width)x$($canvas.Height)); $untouched background pixels verified unchanged."
Write-Output "Saved $uploadPath (1600x900 upload copy)."
$canvas.Dispose(); $overlay.Dispose(); $source.Dispose()
