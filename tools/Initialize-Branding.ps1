param([string]$Directory = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# The public repository contains drawing instructions, not proprietary game sprites.
# Missing files get original gold-bar artwork. Existing local sprite 4529 files
# are deliberately left alone, so a build never replaces someone's own artwork.
$iconDirectory = Join-Path $Directory 'src/DAGoldManager.Desktop/Resources/AppIcon'
$webDirectory = Join-Path $Directory 'src/DAGoldManager.UI/wwwroot/images'
[IO.Directory]::CreateDirectory($iconDirectory) | Out-Null
[IO.Directory]::CreateDirectory($webDirectory) | Out-Null

function New-GoldBarBitmap([int]$Size) {
    $bitmap = [Drawing.Bitmap]::new($Size, $Size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#11161c'))
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($Size / 64.0, $Size / 64.0)
        # Coordinates describe the top, front, and right face of an original ingot.
        $faces = @(
            @{ Color = '#edcf83'; Points = @(14,23, 44,16, 53,29, 18,38) },
            @{ Color = '#bc8734'; Points = @(18,38, 53,29, 51,43, 20,51) },
            @{ Color = '#916226'; Points = @(14,23, 18,38, 20,51, 11,37) }
        )
        foreach ($face in $faces) {
            $points = [Drawing.PointF[]]::new($face.Points.Count / 2)
            for ($index = 0; $index -lt $points.Length; $index++) {
                $points[$index] = [Drawing.PointF]::new($face.Points[$index * 2], $face.Points[$index * 2 + 1])
            }
            $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($face.Color))
            try { $graphics.FillPolygon($brush, $points) } finally { $brush.Dispose() }
        }
    } finally { $graphics.Dispose() }
    return $bitmap
}

function Save-GoldBarPng([string]$Path, [int]$Size) {
    if (Test-Path -LiteralPath $Path) { return }
    $bitmap = New-GoldBarBitmap $Size
    try { $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
}

Save-GoldBarPng (Join-Path $webDirectory 'gold-bar.png') 64
Save-GoldBarPng (Join-Path $iconDirectory 'appicon.png') 512
$iconPath = Join-Path $iconDirectory 'appicon.ico'
if (-not (Test-Path -LiteralPath $iconPath)) {
    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $payloads = foreach ($size in $sizes) {
        $bitmap = New-GoldBarBitmap $size
        $buffer = [IO.MemoryStream]::new()
        try {
            $bitmap.Save($buffer, [Drawing.Imaging.ImageFormat]::Png)
            # Keep each byte array as one object instead of enumerating its bytes.
            ,$buffer.ToArray()
        } finally { $bitmap.Dispose(); $buffer.Dispose() }
    }
    $stream = [IO.File]::Create($iconPath)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        # ICO header, followed by one directory entry per embedded PNG.
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $writer.Write([byte]($sizes[$index] % 256))
            $writer.Write([byte]($sizes[$index] % 256))
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$payloads[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $payloads[$index].Length
        }
        foreach ($payload in $payloads) { $writer.Write([byte[]]$payload) }
    } finally { $writer.Dispose() }
}
