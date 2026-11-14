$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'app-icon.png'))
# Square crop around the approved artwork (solid bounds: 104,83 to 1113,1163).
# Keep the original master and silhouette; reduce padding to about 1% vertically.
$sourceArea = New-Object Drawing.RectangleF 56, 71, 1104, 1104
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = @()
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object Drawing.Bitmap $size, $size
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = New-Object IO.MemoryStream
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, (New-Object Drawing.RectangleF 0, 0, $size, $size), $sourceArea, [Drawing.GraphicsUnit]::Pixel)
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $bytes = $stream.ToArray()
            [IO.File]::WriteAllBytes((Join-Path $PSScriptRoot "app-icon-$size.png"), $bytes)
            $frames += ,$bytes
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $file = [IO.File]::Create((Join-Path $PSScriptRoot 'app.ico'))
    $writer = New-Object IO.BinaryWriter $file
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = $sizes[$i] % 256
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose(); $file.Dispose() }
} finally { $source.Dispose() }
