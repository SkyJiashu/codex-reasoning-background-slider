[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$StartupMovie,
    [string]$SliderVideo=''
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if([string]::IsNullOrWhiteSpace($SliderVideo)){$SliderVideo=Join-Path $root 'build/readme-demo/slider.webm'}
$startup=(Resolve-Path -LiteralPath $StartupMovie).Path
$slider=(Resolve-Path -LiteralPath $SliderVideo).Path
$ffmpeg=(Get-Command ffmpeg -ErrorAction Stop).Source
$ffprobe=(Get-Command ffprobe -ErrorAction Stop).Source
$json=& $ffprobe -v error -select_streams v:0 -show_entries stream=width,height -of json $startup
if($LASTEXITCODE -ne 0){throw 'Could not inspect the supplied cached startup movie.'}
$video=($json|ConvertFrom-Json).streams[0]
$width=[int]$video.width;$height=[int]$video.height
if($width/$height -gt 16.0/9){$cropHeight=$height;$cropWidth=[int][Math]::Floor($height*16.0/9)}else{$cropWidth=$width;$cropHeight=[int][Math]::Floor($width*9.0/16)}
$left=[int][Math]::Floor(($width-$cropWidth)/2);$top=[int][Math]::Floor(($height-$cropHeight)/2)
$output=Join-Path $root 'docs/media'
[IO.Directory]::CreateDirectory($output)|Out-Null
$startupFilter="fps=15,crop=${cropWidth}:${cropHeight}:${left}:${top},scale=800:-1:flags=lanczos,tpad=stop_mode=clone:stop_duration=0.4,split[s0][s1];[s0]palettegen=max_colors=256:reserve_transparent=0:stats_mode=diff[p];[s1][p]paletteuse=dither=none"
& $ffmpeg -hide_banner -loglevel error -y -i $startup -filter_complex $startupFilter -loop 0 (Join-Path $output 'startup.gif')
if($LASTEXITCODE -ne 0){throw 'Startup GIF encoding failed.'}
$sliderFilter='fps=12,scale=800:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=256:reserve_transparent=0:stats_mode=diff[p];[s1][p]paletteuse=dither=none'
& $ffmpeg -hide_banner -loglevel error -y -i $slider -filter_complex $sliderFilter -loop 0 (Join-Path $output 'reasoning-slider.gif')
if($LASTEXITCODE -ne 0){throw 'Slider GIF encoding failed.'}
Get-Item -LiteralPath (Join-Path $output 'startup.gif'),(Join-Path $output 'reasoning-slider.gif')|Select-Object Name,Length
