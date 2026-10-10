# Uygulama simgesini cizer: Assets\raf.ico (16-256 px) ve Assets\raf.png (64 px).
# Kullanim: powershell -ExecutionPolicy Bypass -File tools\simge-uret.ps1
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\Raf.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-RafBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Yuvarlak koseli zemin
    $d = [single]($s * 0.44)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($s - $d, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d, $s - $d, $d, $d, 0, 90)
    $path.AddArc(0, $s - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.Color]::FromArgb(255, 108, 140, 255),
        [System.Drawing.Color]::FromArgb(255, 138, 108, 255), 45.0)
    $g.FillPath($bg, $path)

    $shelf = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $book = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(215, 255, 255, 255))

    # Iki raf ve ustlerinde duran kutular (0..1 olceginde: x, y, genislik, yukseklik)
    $shelves = @(@(0.18, 0.47, 0.64, 0.065), @(0.18, 0.76, 0.64, 0.065))
    $books = @(
        @(0.24, 0.22, 0.11, 0.25), @(0.39, 0.28, 0.11, 0.19), @(0.54, 0.18, 0.11, 0.29),
        @(0.39, 0.585, 0.11, 0.175), @(0.54, 0.555, 0.11, 0.205), @(0.69, 0.615, 0.11, 0.145)
    )
    foreach ($b in $books) { $g.FillRectangle($book, [single]($b[0] * $s), [single]($b[1] * $s), [single]($b[2] * $s), [single]($b[3] * $s)) }
    foreach ($r in $shelves) { $g.FillRectangle($shelf, [single]($r[0] * $s), [single]($r[1] * $s), [single]($r[2] * $s), [single]($r[3] * $s)) }

    $g.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# 256 px disindaki boyutlar klasik (sikistirilmamis) bicimde yazilir; her arac bunu okur.
function Get-DibBytes($bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([math]::Ceiling($s / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 48, 64, 256
$images = foreach ($s in $sizes) {
    $bmp = New-RafBitmap $s
    $data = if ($s -eq 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Data = $data }
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -eq 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$img.Data.Length); $w.Write([int]$offset)
    $offset += $img.Data.Length
}
foreach ($img in $images) { $w.Write($img.Data) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'raf.ico'), $out.ToArray())

$png = New-RafBitmap 64
$png.Save((Join-Path $assets 'raf.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()

"raf.ico ($($out.Length) bayt) ve raf.png yazildi: $((Resolve-Path $assets).Path)"
