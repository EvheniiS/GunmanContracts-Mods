$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourcePath = Join-Path $PSScriptRoot 'VirtualDesktop.Android-20261002-143810.jpg'
$outputPath = Join-Path $PSScriptRoot 'DeathDetails-Nexus-thumbnail-distressed-text-only.png'
$source = [System.Drawing.Bitmap]::new($sourcePath)
$canvas = [System.Drawing.Bitmap]::new($source)
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$scale = $canvas.Width / 1672.0
$graphics.ScaleTransform($scale, $scale)

function Add-TitleText([string]$text, [string]$family, [float]$x, [float]$y, [float]$width, [float]$height, [string]$color, [bool]$italic = $false) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new([System.Drawing.Drawing2D.FillMode]::Winding)
    $fontFamily = [System.Drawing.FontFamily]::new($family)
    $style = [System.Drawing.FontStyle]::Regular
    if ($italic) { $style = [System.Drawing.FontStyle]::Italic }
    $path.AddString($text, $fontFamily, [int]$style, 100, [System.Drawing.PointF]::new(0,0), [System.Drawing.StringFormat]::GenericTypographic)
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
    $black = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(220, 0, 0, 0))
    $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(210, 0, 0, 0), 2)
    $outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $fill = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($color))
    foreach ($shadowWidth in @(24, 18, 12, 6)) {
        $softShadow = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(28, 0, 0, 0), $shadowWidth)
        $softShadow.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $graphics.DrawPath($softShadow, $shadow)
        $softShadow.Dispose()
    }
    $graphics.FillPath($black, $shadow)
    $graphics.DrawPath($outline, $path)
    $graphics.FillPath($fill, $path)
    if ($italic) {
        # Weathering exists strictly inside letter contours; the screenshot is untouched.
        $savedState = $graphics.Save()
        $graphics.SetClip($path, [System.Drawing.Drawing2D.CombineMode]::Intersect)
        $highlight = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.RectangleF]::new($x, $y, $width, $height),
            [System.Drawing.Color]::FromArgb(90, 255, 255, 255),
            [System.Drawing.Color]::FromArgb(0, 255, 255, 255), 90.0)
        $graphics.FillPath($highlight, $path)
        $highlight.Dispose()
        $random = [System.Random]::new(401 + [int]$y)
        for ($i = 0; $i -lt 290; $i++) {
            $px = $x + $random.NextDouble()*$width
            $py = $y + $random.NextDouble()*$height
            $crack = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb($random.Next(35,115), 150, 76, 15), [single](0.3 + $random.NextDouble()*0.8))
            $points = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new($px, $py))
            for ($segment = 0; $segment -lt $random.Next(2,6); $segment++) {
                $px += $random.Next(-5, 15)
                $py -= $random.Next(3, 16)
                $points += [System.Drawing.PointF]::new($px, $py)
            }
            $graphics.DrawLines($crack, [System.Drawing.PointF[]]$points)
            $crack.Dispose()
        }
        for ($i = 0; $i -lt 1600; $i++) {
            $px = $x + $random.NextDouble()*$width
            $py = $y + $random.NextDouble()*$height
            $grain = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($random.Next(35,145), 115, 58, 13))
            $size = [single](0.4 + [Math]::Pow($random.NextDouble(), 5)*3)
            $graphics.FillEllipse($grain, [single]$px, [single]$py, $size, [single]($size*1.6))
            $grain.Dispose()
        }
        $graphics.Restore($savedState)
    }
    $fill.Dispose(); $outline.Dispose(); $black.Dispose(); $shadow.Dispose(); $matrix.Dispose(); $path.Dispose(); $fontFamily.Dispose()
}

# Tight text overlays only: no image resizing, cropping, grading, or regeneration.
Add-TitleText 'G U N M A N   C O N T R A C T S' 'Bahnschrift Condensed' 65 115 515 31 '#FFF1D8'
Add-TitleText 'DEATH' 'Impact' 55 175 600 220 '#FFF1D8' $true
Add-TitleText 'DETAILS' 'Impact' 55 418 615 178 '#FFB800' $true
$rule = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#FFB800'))
$graphics.FillRectangle($rule, 55, 615, 615, 7)
$rule.Dispose()
Add-TitleText 'ENEMIES CLOSE THEIR EYES ON DEATH.' 'Impact' 55 644 615 44 '#FFF1D8'
$graphics.Dispose()
$canvas.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

# Verify the complete right side and the face region retain original decoded pixels.
function Get-RegionHash($bitmap, $rectangle) {
    $crop = $bitmap.Clone($rectangle, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stream = [System.IO.MemoryStream]::new()
    $crop.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = [System.BitConverter]::ToString($sha.ComputeHash($stream.ToArray()))
    $sha.Dispose(); $stream.Dispose(); $crop.Dispose()
    return $hash
}
$regions = @(
    [System.Drawing.Rectangle]::new([int](700*$scale), 0, $canvas.Width-[int](700*$scale), $canvas.Height),
    [System.Drawing.Rectangle]::new([int](700*$scale), [int](100*$scale), [int](330*$scale), [int](450*$scale))
)
foreach ($region in $regions) {
    if ((Get-RegionHash $source $region) -ne (Get-RegionHash $canvas $region)) { throw 'Original-image preservation check failed.' }
}
Write-Output "Saved $outputPath ($($canvas.Width)x$($canvas.Height)). Original face, closed eyes, and complete right side verified pixel-identical."
$canvas.Dispose(); $source.Dispose()


