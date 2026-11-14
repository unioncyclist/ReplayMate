Add-Type -AssemblyName System.Drawing

# Classic DIB (32bpp + AND mask) multi-size ICO. Universal compatibility.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$entries = New-Object System.Collections.ArrayList

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [math]::Max(0, [int]($s * 0.03))
    $side = $s - 2 * $pad
    $radius = [int]($side * 0.22)
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, $side, $side)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d2 = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d2, $d2, 180, 90)
    $path.AddArc($rect.Right - $d2, $rect.Y, $d2, $d2, 270, 90)
    $path.AddArc($rect.Right - $d2, $rect.Bottom - $d2, $d2, $d2, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d2, $d2, $d2, 90, 90)
    $path.CloseFigure()

    $red = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 222, 45, 38))
    $g.FillPath($red, $path)

    $fontSize = [float]($side * 0.80)
    $font = New-Object System.Drawing.Font('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.DrawString('R', $font, $white, (New-Object System.Drawing.RectangleF(0, 0, $s, $s)), $fmt)

    $g.Dispose()

    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle(0, 0, $s, $s)),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $raw = New-Object byte[] ($stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
    $bmp.UnlockBits($data)
    $bmp.Dispose()

    # BITMAPINFOHEADER + bottom-up pixels + AND mask
    $maskStride = [int](([math]::Ceiling($s / 8.0) + 3) / 4) * 4
    $maskSize = $maskStride * $s
    $pixelSize = $s * $s * 4
    $imgSize = 40 + $pixelSize + $maskSize

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([Int32]40)
    $bw.Write([Int32]$s)
    $bw.Write([Int32]($s * 2))
    $bw.Write([Int16]1)
    $bw.Write([Int16]32)
    $bw.Write([Int32]0)
    $bw.Write([Int32]($pixelSize + $maskSize))
    $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([Int32]0)

    for ($y = $s - 1; $y -ge 0; $y--) {
        $bw.Write($raw, $y * $stride, $s * 4)
    }
    $bw.Write((New-Object byte[] $maskSize))
    $bw.Flush()
    [void]$entries.Add(@{ Size = $s; Bytes = $ms.ToArray() })
    $ms.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$entries.Count)

$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$e.Bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $bw.Write($e.Bytes) }
$bw.Flush()

$path = Join-Path $PSScriptRoot 'Assets\replaymate.ico'
[System.IO.File]::WriteAllBytes($path, $out.ToArray())
$bw.Dispose(); $out.Dispose()

$check = New-Object System.Drawing.Icon($path)
Write-Output ("icon written: {0} bytes, sizes {1}, loaded {2}x{3}" -f (Get-Item $path).Length, ($sizes -join '/'), $check.Width, $check.Height)
