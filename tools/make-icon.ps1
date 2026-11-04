# Regenerates assets/app-icon.ico from the Light artwork. Run by hand when the artwork changes.
# Small sizes crop to the head so the face stays readable on the taskbar.
[CmdletBinding()]
param([double]$HeadShare=0.60)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$art=[Drawing.Bitmap]::new((Join-Path $projectRoot 'assets/stages-v10-review/01_Light.png'))
function Bounds($bitmap,[int]$lastRow) {
    $left=$bitmap.Width;$right=-1;$top=$bitmap.Height;$bottom=-1
    for($y=0;$y -le $lastRow;$y++){for($x=0;$x -lt $bitmap.Width;$x++){
        if($bitmap.GetPixel($x,$y).A -gt 32){if($x -lt $left){$left=$x};if($x -gt $right){$right=$x};if($y -lt $top){$top=$y};if($y -gt $bottom){$bottom=$y}}
    }}
    return [Drawing.Rectangle]::FromLTRB($left,$top,$right+1,$bottom+1)
}
function Square($source,$rect,[int]$size) {
    $frame=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $canvas=[Drawing.Graphics]::FromImage($frame)
    $canvas.InterpolationMode='HighQualityBicubic';$canvas.PixelOffsetMode='HighQuality';$canvas.CompositingQuality='HighQuality'
    $scale=$size/[Math]::Max($rect.Width,$rect.Height)
    $width=$rect.Width*$scale;$height=$rect.Height*$scale
    $canvas.DrawImage($source,[Drawing.RectangleF]::new(($size-$width)/2,($size-$height)/2,$width,$height),[Drawing.RectangleF]$rect,'Pixel')
    $canvas.Dispose();return $frame
}
function Png($bitmap) {$stream=[IO.MemoryStream]::new();$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png);return $stream.ToArray()}
function Dib($bitmap) {
    # 32bpp bottom-up DIB plus an empty AND mask: the form every Windows icon consumer reads.
    $size=$bitmap.Width;$mask=[int]([Math]::Ceiling($size/32.0)*4)*$size
    $stream=[IO.MemoryStream]::new();$out=[IO.BinaryWriter]::new($stream)
    $out.Write([int]40);$out.Write([int]$size);$out.Write([int]($size*2));$out.Write([int16]1);$out.Write([int16]32);$out.Write([int]0);$out.Write([int]($size*$size*4+$mask));$out.Write([int]0);$out.Write([int]0);$out.Write([int]0);$out.Write([int]0)
    for($y=$size-1;$y -ge 0;$y--){for($x=0;$x -lt $size;$x++){$c=$bitmap.GetPixel($x,$y);$out.Write([byte]$c.B);$out.Write([byte]$c.G);$out.Write([byte]$c.R);$out.Write([byte]$c.A)}}
    $out.Write([byte[]]::new($mask));$out.Flush();return $stream.ToArray()
}
try {
    $body=Bounds $art ($art.Height-1)
    $head=Bounds $art ([int]($body.Top+$body.Height*$HeadShare))
    $frames=foreach($size in 16,24,32,48,64,256) {
        $bitmap=Square $art $(if($size -le 48){$head}else{$body}) $size
        [pscustomobject]@{Size=$size;Data=$(if($size -eq 256){Png $bitmap}else{Dib $bitmap})}
        $bitmap.Dispose()
    }
    $target=Join-Path $projectRoot 'assets/app-icon.ico'
    $stream=[IO.MemoryStream]::new();$out=[IO.BinaryWriter]::new($stream)
    $out.Write([int16]0);$out.Write([int16]1);$out.Write([int16]$frames.Count)
    $offset=6+16*$frames.Count
    foreach($frame in $frames){
        $edge=if($frame.Size -eq 256){0}else{$frame.Size}
        $out.Write([byte]$edge);$out.Write([byte]$edge);$out.Write([byte]0);$out.Write([byte]0);$out.Write([int16]1);$out.Write([int16]32);$out.Write([int]$frame.Data.Length);$out.Write([int]$offset)
        $offset+=$frame.Data.Length
    }
    foreach($frame in $frames){$out.Write([byte[]]$frame.Data)}
    $out.Flush();[IO.File]::WriteAllBytes($target,$stream.ToArray())
    Write-Output "Icon written: $target (art $($art.Width)x$($art.Height), body $body, head $head)"
} finally {$art.Dispose()}
