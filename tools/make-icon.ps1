# Makes src\Aion2Meter.App\app.ico from a square-ish source image (png/webp): trims the white margin,
# makes the outside of the rounded square transparent and writes 16..256 px sizes.
#   tools\make-icon.ps1 -Source <image> [-Output <ico>]
param([Parameter(Mandatory)][string]$Source, [string]$Output)
$ErrorActionPreference = 'Stop'
if (-not $Output) { $Output = Join-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) '..\src\Aion2Meter.App\app.ico' }
Add-Type -AssemblyName PresentationCore, System.Drawing
Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Drawing; using System.Drawing.Imaging;
public static class IconMaker {
  // Outside = near-white pixels connected to the image border.
  public static Bitmap Cut(Bitmap src) {
    int w = src.Width, h = src.Height;
    var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, w, h);
    var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    var px = new byte[data.Stride * h]; System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
    var seen = new bool[w * h]; var q = new Queue<int>();
    Func<int, bool> white = i => { int o = (i / w) * data.Stride + (i % w) * 4; return Math.Min(px[o], Math.Min(px[o + 1], px[o + 2])) >= 225; };
    for (int x = 0; x < w; x++) { q.Enqueue(x); q.Enqueue((h - 1) * w + x); }
    for (int y = 0; y < h; y++) { q.Enqueue(y * w); q.Enqueue(y * w + w - 1); }
    while (q.Count > 0) {
      int i = q.Dequeue(); if (i < 0 || i >= w * h || seen[i] || !white(i)) continue; seen[i] = true;
      int x = i % w; if (x > 0) q.Enqueue(i - 1); if (x < w - 1) q.Enqueue(i + 1); q.Enqueue(i - w); q.Enqueue(i + w);
    }
    int minX = w, minY = h, maxX = 0, maxY = 0;
    for (int i = 0; i < w * h; i++) {
      int o = (i / w) * data.Stride + (i % w) * 4;
      if (seen[i]) { px[o + 3] = 0; continue; }
      int x = i % w, y = i / w; if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
    }
    System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length); bmp.UnlockBits(data);
    int side = Math.Max(maxX - minX + 1, maxY - minY + 1);
    var sq = new Bitmap(side, side, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(sq)) g.DrawImage(bmp, new Rectangle((side - (maxX - minX + 1)) / 2, (side - (maxY - minY + 1)) / 2, maxX - minX + 1, maxY - minY + 1), minX, minY, maxX - minX + 1, maxY - minY + 1, GraphicsUnit.Pixel);
    return sq;
  }
}
'@ -ReferencedAssemblies System.Drawing

$stream = [IO.File]::OpenRead((Resolve-Path $Source))
$frame = [Windows.Media.Imaging.BitmapDecoder]::Create($stream, 'None', 'OnLoad').Frames[0]
$png = New-Object Windows.Media.Imaging.PngBitmapEncoder; $png.Frames.Add($frame)
$mem = New-Object IO.MemoryStream; $png.Save($mem); $stream.Close(); $mem.Position = 0
$square = [IconMaker]::Cut([Drawing.Bitmap]::FromStream($mem))

$sizes = 256, 128, 64, 48, 32, 24, 16
# 256 px as PNG, smaller sizes as classic BMP entries (System.Drawing.Icon, used by the tray, cannot read small PNG entries).
function Get-Dib($b) {
    $s = $b.Width
    $d = $b.LockBits([Drawing.Rectangle]::new(0, 0, $s, $s), 'ReadOnly', [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $px = New-Object byte[] ($d.Stride * $s); [Runtime.InteropServices.Marshal]::Copy($d.Scan0, $px, 0, $px.Length); $b.UnlockBits($d)
    $m = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $m
    $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2)); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]0); $bw.Write([uint32]($s * $s * 4)); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
    for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($px, $y * $d.Stride, $s * 4) }
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    , $m.ToArray()
}
$images = foreach ($s in $sizes) {
    $b = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($b)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'
    $g.DrawImage($square, 0, 0, $s, $s); $g.Dispose()
    if ($s -ge 256) { $m = New-Object IO.MemoryStream; $b.Save($m, [Drawing.Imaging.ImageFormat]::Png); , $m.ToArray() }
    else { Get-Dib $b }
    $b.Dispose()
}
$out = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Output), $out.ToArray())
"wrote $Output ($($out.Length) bytes, sizes $($sizes -join ','))"
