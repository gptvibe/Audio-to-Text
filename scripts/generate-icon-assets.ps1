param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePng
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $repoRoot "src\App.Desktop\Assets"
$sourceCopy = Join-Path $assetsDir "QuietScribeIconSource.png"

Add-Type -AssemblyName System.Drawing

function Save-ResizedPng {
    param(
        [System.Drawing.Image]$Source,
        [string]$Path,
        [int]$Width,
        [int]$Height,
        [int]$Inset = 0
    )

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $targetWidth = $Width - ($Inset * 2)
    $targetHeight = $Height - ($Inset * 2)
    $sourceRatio = $Source.Width / $Source.Height
    $targetRatio = $targetWidth / $targetHeight

    if ($sourceRatio -gt $targetRatio) {
        $drawWidth = $targetWidth
        $drawHeight = [int]($targetWidth / $sourceRatio)
    }
    else {
        $drawHeight = $targetHeight
        $drawWidth = [int]($targetHeight * $sourceRatio)
    }

    $x = [int](($Width - $drawWidth) / 2)
    $y = [int](($Height - $drawHeight) / 2)
    $graphics.DrawImage($Source, $x, $y, $drawWidth, $drawHeight)
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)

    $graphics.Dispose()
    $bitmap.Dispose()
}

function New-IconFile {
    param(
        [System.Drawing.Image]$Source,
        [string]$Path
    )

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $entries = @()
    $imageBytes = @()

    foreach ($size in $sizes) {
        $stream = New-Object System.IO.MemoryStream
        $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($Source, 0, 0, $size, $size)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $stream.ToArray()
        $imageBytes += ,$bytes
        $entries += [PSCustomObject]@{
            Size = $size
            Length = $bytes.Length
        }
        $graphics.Dispose()
        $bitmap.Dispose()
        $stream.Dispose()
    }

    $output = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter $output
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $widthByte = if ($size -eq 256) { 0 } else { $size }
        $writer.Write([byte]$widthByte)
        $writer.Write([byte]$widthByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$imageBytes[$i].Length)
        $writer.Write([UInt32]$offset)
        $offset += $imageBytes[$i].Length
    }

    foreach ($bytes in $imageBytes) {
        $writer.Write($bytes)
    }

    $writer.Dispose()
    $output.Dispose()
}

Copy-Item -LiteralPath $SourcePng -Destination $sourceCopy -Force
$sourceImage = [System.Drawing.Image]::FromFile($sourceCopy)

try {
    Save-ResizedPng -Source $sourceImage -Path (Join-Path $assetsDir "Square44x44Logo.scale-200.png") -Width 88 -Height 88
    Save-ResizedPng -Source $sourceImage -Path (Join-Path $assetsDir "Square44x44Logo.targetsize-24_altform-unplated.png") -Width 24 -Height 24
    Save-ResizedPng -Source $sourceImage -Path (Join-Path $assetsDir "Square150x150Logo.scale-200.png") -Width 300 -Height 300
    Save-ResizedPng -Source $sourceImage -Path (Join-Path $assetsDir "StoreLogo.png") -Width 50 -Height 50
    Save-ResizedPng -Source $sourceImage -Path (Join-Path $assetsDir "LockScreenLogo.scale-200.png") -Width 48 -Height 48
    Save-ResizedPng -Source $sourceImage -Path (Join-Path $assetsDir "Wide310x150Logo.scale-200.png") -Width 620 -Height 300 -Inset 84
    Save-ResizedPng -Source $sourceImage -Path (Join-Path $assetsDir "SplashScreen.scale-200.png") -Width 1240 -Height 600 -Inset 198
    New-IconFile -Source $sourceImage -Path (Join-Path $assetsDir "AppIcon.ico")
}
finally {
    $sourceImage.Dispose()
}

Get-ChildItem -LiteralPath $assetsDir -File |
    Where-Object { $_.Name -in @("AppIcon.ico", "QuietScribeIconSource.png", "Square44x44Logo.scale-200.png", "Square150x150Logo.scale-200.png", "StoreLogo.png") } |
    Select-Object Name, Length
