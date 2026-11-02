<#
.SYNOPSIS
    Generates the Inactive icons from code, with no external tools.

.DESCRIPTION
    Draws the app icon, the locked tray icon and the README logo with System.Drawing. Each size is
    drawn on its own with hand picked geometry instead of being scaled down, so the small sizes
    stay sharp in the tray. The .ico files hold one PNG per size.

    Works in Windows PowerShell 5.1 and PowerShell 7 on Windows.

.PARAMETER OutputRoot
    The repository root to write into. Defaults to the parent of this script's folder.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\generate-icons.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Resolved here because Windows PowerShell 5.1 leaves $PSScriptRoot empty in parameter defaults.
if (-not $OutputRoot) {
    $OutputRoot = Split-Path -Parent $PSScriptRoot
}

Add-Type -AssemblyName System.Drawing

# Every value is in pixels of the final icon. Ring and Dot are even so that their edges land on
# pixel boundaries around the exact centre of the tile, which is what keeps 16 px crisp.
$Frames = @(
    @{ Size = 16;  Margin = 0;  Radius = 3;  Ring = 12;  Stroke = 2;  Dot = 4 }
    @{ Size = 20;  Margin = 0;  Radius = 4;  Ring = 14;  Stroke = 2;  Dot = 4 }
    @{ Size = 24;  Margin = 1;  Radius = 5;  Ring = 14;  Stroke = 2;  Dot = 4 }
    @{ Size = 32;  Margin = 1;  Radius = 7;  Ring = 20;  Stroke = 3;  Dot = 6 }
    @{ Size = 40;  Margin = 2;  Radius = 8;  Ring = 24;  Stroke = 3;  Dot = 8 }
    @{ Size = 48;  Margin = 2;  Radius = 10; Ring = 28;  Stroke = 4;  Dot = 8 }
    @{ Size = 64;  Margin = 3;  Radius = 13; Ring = 38;  Stroke = 5;  Dot = 12 }
    @{ Size = 128; Margin = 6;  Radius = 26; Ring = 76;  Stroke = 10; Dot = 24 }
    @{ Size = 256; Margin = 12; Radius = 52; Ring = 152; Stroke = 20; Dot = 48 }
)

$Palette = @{
    Idle   = @{ Top = '#4F46E5'; Bottom = '#312E81' }
    Locked = @{ Top = '#7C6CF7'; Bottom = '#4F46E5' }
}

$HighlightMinimumSize = 64

$White = [System.Drawing.Color]::White

function ConvertTo-Color([string] $Hex) {
    [System.Drawing.ColorTranslator]::FromHtml($Hex)
}

function New-RoundedSquarePath([single] $Left, [single] $Length, [single] $Radius) {
    $diameter = $Radius * 2
    $far = $Left + $Length - $diameter

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($Left, $Left, $diameter, $diameter, 180, 90)
    $path.AddArc($far, $Left, $diameter, $diameter, 270, 90)
    $path.AddArc($far, $far, $diameter, $diameter, 0, 90)
    $path.AddArc($Left, $far, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Add-Circle([System.Drawing.Drawing2D.GraphicsPath] $Path, [single] $Centre, [single] $Diameter) {
    $Path.AddEllipse($Centre - $Diameter / 2, $Centre - $Diameter / 2, $Diameter, $Diameter)
}

# A faint light edge along the top of the tile. It lifts the tile off dark backgrounds at the sizes
# where a one pixel line reads as an edge and not as noise.
function Add-TileHighlight([System.Drawing.Graphics] $Graphics, [hashtable] $Frame, [System.Drawing.RectangleF] $Bounds) {
    $width = [single] ($Frame.Size / 128)
    $inset = $width / 2
    $edge = New-RoundedSquarePath ($Frame.Margin + $inset) ($Bounds.Width - $width) ($Frame.Radius - $inset)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $Bounds,
        [System.Drawing.Color]::FromArgb(56, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $pen = New-Object System.Drawing.Pen($brush, $width)

    try {
        $Graphics.DrawPath($pen, $edge)
    }
    finally {
        $pen.Dispose()
        $brush.Dispose()
        $edge.Dispose()
    }
}

function New-Frame([hashtable] $Frame, [hashtable] $Colors, [bool] $Filled) {
    $size = $Frame.Size
    $tileLength = $size - 2 * $Frame.Margin
    $centre = $size / 2

    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $tile = New-RoundedSquarePath $Frame.Margin $tileLength $Frame.Radius
    $tileBounds = New-Object System.Drawing.RectangleF($Frame.Margin, $Frame.Margin, $tileLength, $tileLength)
    $tileBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $tileBounds,
        (ConvertTo-Color $Colors.Top),
        (ConvertTo-Color $Colors.Bottom),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $whiteBrush = New-Object System.Drawing.SolidBrush($White)

    try {
        $graphics.FillPath($tileBrush, $tile)

        if ($size -ge $HighlightMinimumSize) {
            Add-TileHighlight $graphics $Frame $tileBounds
        }

        $mark = New-Object System.Drawing.Drawing2D.GraphicsPath
        try {
            if ($Filled) {
                # The even-odd fill rule turns the inner circle into a hole that shows the tile.
                Add-Circle $mark $centre $Frame.Ring
                Add-Circle $mark $centre $Frame.Dot
            }
            else {
                Add-Circle $mark $centre $Frame.Ring
                Add-Circle $mark $centre ($Frame.Ring - 2 * $Frame.Stroke)
                Add-Circle $mark $centre $Frame.Dot
            }

            $graphics.FillPath($whiteBrush, $mark)
        }
        finally {
            $mark.Dispose()
        }

        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return , $stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $whiteBrush.Dispose()
        $tileBrush.Dispose()
        $tile.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-IconFile([string] $Path, [object[]] $Images) {
    $directoryLength = 6
    $entryLength = 16
    $offset = $directoryLength + $entryLength * $Images.Count

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16] 0)              # Reserved
        $writer.Write([uint16] 1)              # Type: icon
        $writer.Write([uint16] $Images.Count)

        foreach ($image in $Images) {
            # A width or height of 256 does not fit in a byte, so the format stores it as 0.
            $dimension = [byte] ($image.Size % 256)

            $writer.Write($dimension)
            $writer.Write($dimension)
            $writer.Write([byte] 0)            # Palette size: none
            $writer.Write([byte] 0)            # Reserved
            $writer.Write([uint16] 1)          # Colour planes
            $writer.Write([uint16] 32)         # Bits per pixel
            $writer.Write([uint32] $image.Png.Length)
            $writer.Write([uint32] $offset)

            $offset += $image.Png.Length
        }

        foreach ($image in $Images) {
            $writer.Write([byte[]] $image.Png)
        }

        $writer.Flush()
        [System.IO.File]::WriteAllBytes($Path, $stream.ToArray())
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function New-IconSet([hashtable] $Colors, [bool] $Filled) {
    foreach ($frame in $Frames) {
        [pscustomobject] @{
            Size = $frame.Size
            Png  = New-Frame $frame $Colors $Filled
        }
    }
}

$iconDirectory = Join-Path $OutputRoot 'src\Inactive\Assets'
$assetDirectory = Join-Path $OutputRoot 'assets'
[void] (New-Item -ItemType Directory -Force -Path $iconDirectory, $assetDirectory)

$idle = @(New-IconSet $Palette.Idle $false)
$locked = @(New-IconSet $Palette.Locked $true)

$idlePath = Join-Path $iconDirectory 'Inactive.ico'
$lockedPath = Join-Path $iconDirectory 'InactiveLocked.ico'
$logoPath = Join-Path $assetDirectory 'logo.png'

Write-IconFile $idlePath $idle
Write-IconFile $lockedPath $locked
[System.IO.File]::WriteAllBytes($logoPath, [byte[]] ($idle | Where-Object { $_.Size -eq 256 }).Png)

foreach ($path in $idlePath, $lockedPath, $logoPath) {
    Write-Output ('{0} ({1:N0} bytes)' -f $path, (Get-Item -LiteralPath $path).Length)
}
