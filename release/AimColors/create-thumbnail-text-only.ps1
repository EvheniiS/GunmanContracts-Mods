Add-Type -AssemblyName System.Drawing
$sourcePath = Join-Path $PSScriptRoot 'Header.jpg'
$outputPath = Join-Path $PSScriptRoot 'AimColors-Nexus-thumbnail-text-only.png'
$source = [System.Drawing.Bitmap]::new($sourcePath)
$canvas = [System.Drawing.Bitmap]::new($source)
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$scale = $canvas.Width / 1672.0
$graphics.ScaleTransform($scale, $scale)

function Add-TitleText([string]$text, [string]$family, [float]$x, [float]$y, [float]$width, [float]$height, [string]$color, [bool]$italic = $false) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
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
    $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(230, 0, 0, 0), 8)
    $outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $fill = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($color))
    $graphics.FillPath($black, $shadow)
    $graphics.DrawPath($outline, $path)
    $graphics.FillPath($fill, $path)
    $fill.Dispose(); $outline.Dispose(); $black.Dispose(); $shadow.Dispose(); $matrix.Dispose(); $path.Dispose(); $fontFamily.Dispose()
}

# Tight text overlays only: no image resizing, cropping, grading, or regeneration.
Add-TitleText 'G U N M A N   C O N T R A C T S' 'Bahnschrift Condensed' 65 115 515 31 '#FFF1D8'
Add-TitleText 'AIM' 'Impact' 55 175 510 220 '#FFF1D8' $true
Add-TitleText 'COLORS' 'Impact' 55 418 615 178 '#FFB800' $true
$rule = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#FFB800'))
$graphics.FillRectangle($rule, 55, 615, 615, 7)
$rule.Dispose()
Add-TitleText 'Improved laser and sights.' 'Bahnschrift Condensed' 55 644 615 44 '#FFF1D8'
$graphics.Dispose()
$canvas.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

# Verify the complete right side and the laser-dot region retain original decoded pixels.
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
    [System.Drawing.Rectangle]::new([int](590*$scale), [int](310*$scale), [int](85*$scale), [int](85*$scale))
)
foreach ($region in $regions) {
    if ((Get-RegionHash $source $region) -ne (Get-RegionHash $canvas $region)) { throw 'Original-image preservation check failed.' }
}
Write-Output "Saved $outputPath ($($canvas.Width)x$($canvas.Height)). Original pistol, sights, laser region, and complete right side verified pixel-identical."
$canvas.Dispose(); $source.Dispose()
