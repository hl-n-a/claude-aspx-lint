# ----------------------------------------------------------------
# build-icons.ps1
#
# Genere les declinaisons de l'icone et de la banniere a partir des
# sources dans assets/source/, puis les place dans les bons projets.
#
# Pre-requis : aucun. Utilise System.Drawing (PowerShell sur Windows).
# Idempotent : peut etre re-execute, ecrase les anciennes declinaisons.
#
# Usage :
#     pwsh assets/build-icons.ps1
# ou en CI :
#     pwsh -NoProfile -ExecutionPolicy Bypass -File assets/build-icons.ps1
# ----------------------------------------------------------------
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $repoRoot 'assets/source'
$derivedDir = Join-Path $repoRoot 'assets/derived'
New-Item -ItemType Directory -Force -Path $derivedDir | Out-Null

function Resize-Png {
    param(
        [Parameter(Mandatory)] [string] $InPath,
        [Parameter(Mandatory)] [string] $OutPath,
        [Parameter(Mandatory)] [int]    $Width,
        [Parameter(Mandatory)] [int]    $Height
    )
    $src = [System.Drawing.Image]::FromFile($InPath)
    try {
        $bmp = New-Object System.Drawing.Bitmap $Width, $Height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $g.DrawImage($src, 0, 0, $Width, $Height)
        } finally { $g.Dispose() }
        $bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    } finally { $src.Dispose() }
    Write-Host "  generated $OutPath ($Width x $Height)"
}

function Build-MultiResIco {
    # ICO multi-resolutions avec PNG embedded pour les grosses tailles.
    # Format de l'en-tete .ico :
    #   ICONDIR (6 bytes) : reserved=0 (2), type=1 (2), count=N (2)
    #   N x ICONDIRENTRY (16 bytes) : w, h, palette, reserved, planes, bpp,
    #                                  size_in_bytes, offset_to_image
    #   ... PNG data concatenes ...
    param(
        [Parameter(Mandatory)] [string] $OutPath,
        [Parameter(Mandatory)] [string[]] $PngPaths   # tries du plus petit au plus grand
    )

    $entries = foreach ($p in $PngPaths) {
        $bytes = [System.IO.File]::ReadAllBytes($p)
        $img = [System.Drawing.Image]::FromFile($p)
        try {
            [PSCustomObject]@{
                Width  = if ($img.Width  -ge 256) { 0 } else { $img.Width  }
                Height = if ($img.Height -ge 256) { 0 } else { $img.Height }
                Bytes  = $bytes
            }
        } finally { $img.Dispose() }
    }

    $count = $entries.Count
    $headerSize = 6 + (16 * $count)
    $offset = $headerSize

    $out = [System.IO.MemoryStream]::new()
    $w   = [System.IO.BinaryWriter]::new($out)
    # ICONDIR
    $w.Write([UInt16]0)        # reserved
    $w.Write([UInt16]1)        # type = ICO
    $w.Write([UInt16]$count)   # count

    # ICONDIRENTRYs
    foreach ($e in $entries) {
        $w.Write([byte]$e.Width)
        $w.Write([byte]$e.Height)
        $w.Write([byte]0)            # palette (PNG)
        $w.Write([byte]0)            # reserved
        $w.Write([UInt16]1)          # planes
        $w.Write([UInt16]32)         # bpp
        $w.Write([UInt32]$e.Bytes.Length)
        $w.Write([UInt32]$offset)
        $offset += $e.Bytes.Length
    }
    # Image data
    foreach ($e in $entries) {
        $w.Write($e.Bytes)
    }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($OutPath, $out.ToArray())
    $w.Dispose()
    $out.Dispose()
    Write-Host "  generated $OutPath (.ico, $count resolutions)"
}

function Crop-Banner {
    # AI a livre du 1983x793 (~2.5:1). Marketplace VS Code attend 1376x400
    # (~3.44:1). On crop verticalement les bandes noires en haut/bas en
    # gardant le centre. Resize ensuite a la taille marketplace.
    param(
        [Parameter(Mandatory)] [string] $InPath,
        [Parameter(Mandatory)] [string] $OutPath,
        [int] $TargetWidth = 1376,
        [int] $TargetHeight = 400
    )
    $src = [System.Drawing.Image]::FromFile($InPath)
    try {
        # Calcule la zone de crop pour matcher le ratio cible.
        $targetRatio = $TargetWidth / [double]$TargetHeight
        $srcRatio = $src.Width / [double]$src.Height
        if ($srcRatio -gt $targetRatio) {
            # Source plus large que cible : on coupe les cotes
            $cropH = $src.Height
            $cropW = [int]($src.Height * $targetRatio)
        } else {
            # Source plus haute que cible : on coupe haut/bas
            $cropW = $src.Width
            $cropH = [int]($src.Width / $targetRatio)
        }
        $cropX = [int](($src.Width  - $cropW) / 2)
        $cropY = [int](($src.Height - $cropH) / 2)
        $crop = New-Object System.Drawing.Rectangle $cropX, $cropY, $cropW, $cropH

        $bmp = New-Object System.Drawing.Bitmap $TargetWidth, $TargetHeight
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $dst = New-Object System.Drawing.Rectangle 0, 0, $TargetWidth, $TargetHeight
            $g.DrawImage($src, $dst, $crop, [System.Drawing.GraphicsUnit]::Pixel)
        } finally { $g.Dispose() }
        $bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    } finally { $src.Dispose() }
    Write-Host "  generated $OutPath (banner $TargetWidth x $TargetHeight, cropped from source)"
}

# ================================================================
# 1. Icone : sizes standard + favicon ico
# ================================================================
Write-Host "=== Icone ==="
$iconSrc = Join-Path $sourceDir 'icon-1024.png'
if (-not (Test-Path $iconSrc)) {
    throw "Source manquante : $iconSrc"
}

$sizes = 16, 32, 48, 64, 128, 180, 256, 512, 1024
foreach ($s in $sizes) {
    $out = Join-Path $derivedDir "icon-$s.png"
    Resize-Png -InPath $iconSrc -OutPath $out -Width $s -Height $s
}

# ICO multi-res (16, 32, 48, 64, 128, 256) — typique pour Windows
$icoOut = Join-Path $derivedDir 'icon.ico'
$icoInputs = 16, 32, 48, 64, 128, 256 | ForEach-Object { Join-Path $derivedDir "icon-$_.png" }
Build-MultiResIco -OutPath $icoOut -PngPaths $icoInputs

# Favicon classique (16+32 dans un ico)
$faviconOut = Join-Path $derivedDir 'favicon.ico'
$faviconInputs = 16, 32, 48 | ForEach-Object { Join-Path $derivedDir "icon-$_.png" }
Build-MultiResIco -OutPath $faviconOut -PngPaths $faviconInputs

# ================================================================
# 2. Banniere VS Code marketplace (1376x400)
# ================================================================
Write-Host "=== Banniere ==="
$bannerSrc = Join-Path $sourceDir 'banner-source.png'
if (Test-Path $bannerSrc) {
    Crop-Banner -InPath $bannerSrc -OutPath (Join-Path $derivedDir 'banner-1376x400.png')
    # OG image pour les partages sociaux : 1200x630 (Twitter / OpenGraph)
    Crop-Banner -InPath $bannerSrc -OutPath (Join-Path $derivedDir 'og-image-1200x630.png') -TargetWidth 1200 -TargetHeight 630
}

# ================================================================
# 3. Distribution dans les projets
# ================================================================
Write-Host "=== Cablage ==="

$copies = @(
    # VS Code extension
    @{ src = "$derivedDir/icon-128.png";          dst = "$repoRoot/src/AspxLint.VSCode/icon.png" }
    # Stats site (docs/)
    @{ src = "$derivedDir/favicon.ico";           dst = "$repoRoot/docs/favicon.ico" }
    @{ src = "$derivedDir/icon-180.png";          dst = "$repoRoot/docs/apple-touch-icon.png" }
    @{ src = "$derivedDir/icon-512.png";          dst = "$repoRoot/docs/icon-512.png" }
    @{ src = "$derivedDir/og-image-1200x630.png"; dst = "$repoRoot/docs/og-image.png" }
    # Dashboard web (sert pendant le runtime, embedded dans le serveur)
    @{ src = "$derivedDir/favicon.ico";           dst = "$repoRoot/src/AspxLint.Web/favicon.ico" }
    # Desktop : .ico pour la fenetre + tray
    @{ src = "$derivedDir/icon.ico";              dst = "$repoRoot/src/AspxLint.Desktop/icon.ico" }
)
foreach ($c in $copies) {
    $dstDir = Split-Path -Parent $c.dst
    New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
    Copy-Item -Force -Path $c.src -Destination $c.dst
    Write-Host "  $($c.dst)"
}

Write-Host ""
Write-Host "Done. Verifie 'git status' pour voir les fichiers ajoutes."
