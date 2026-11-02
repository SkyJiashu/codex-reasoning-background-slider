[CmdletBinding()]
param([int]$Width=0,[int]$Height=0)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ffmpeg=(Get-Command ffmpeg -ErrorAction Stop).Source
$ffprobe=(Get-Command ffprobe -ErrorAction Stop).Source
$build=Join-Path $projectRoot 'build/film-render'
[IO.Directory]::CreateDirectory($build) | Out-Null
$renderer=Join-Path $build 'RenderFilm.exe'
$frameworkRoot=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)) 'Microsoft.NET/Framework64/v4.0.30319'
$compiler=Join-Path $frameworkRoot 'csc.exe'
$compileArgs=@('/nologo','/target:exe','/platform:x64','/optimize+','/warnaserror+',('/out:'+$renderer),('/win32manifest:'+(Join-Path $projectRoot 'src/app.manifest')),'/reference:System.Xaml.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll')
foreach($name in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')) {
    $compileArgs+='/reference:'+(Join-Path $frameworkRoot ('WPF/'+$name))
}
$compileArgs+=@((Join-Path $projectRoot 'src/DataScene.cs'),(Join-Path $projectRoot 'tools/RenderFilm.cs'))
& $compiler @compileArgs
if($LASTEXITCODE -ne 0){throw 'Offline film producer compilation failed.'}
$hero=Join-Path $projectRoot 'assets/stages-v11-nose/ultra.png'
# Developer cache generation only. Release packaging must not bundle these local outputs.
$cache=Join-Path $projectRoot 'build/local-film-cache'
[IO.Directory]::CreateDirectory($cache) | Out-Null
$film=Join-Path $cache 'startup.mp4'
$poster=Join-Path $cache 'startup-poster.png'
$end=Join-Path $cache 'startup-end.png'
$values=@($ffmpeg,$hero,$film,$poster,$end)
if($Width -ne 0 -or $Height -ne 0){$values+=@($Width,$Height)}
$renderArgs=$values | ForEach-Object {'"'+$_+'"'}
$log=Join-Path $build 'render.log'
$errorLog=Join-Path $build 'render-error.log'
$process=Start-Process -FilePath $renderer -ArgumentList $renderArgs -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $log -RedirectStandardError $errorLog
if($process.ExitCode -ne 0){throw ('Local cache rendering failed. See '+$errorLog)}
$metadataText=& $ffprobe -v error -show_entries 'stream=codec_type,codec_name,profile,level,pix_fmt,color_range,width,height,r_frame_rate,avg_frame_rate,nb_frames,duration:format=duration,size' -of json $film
if($LASTEXITCODE -ne 0){throw 'ffprobe verification failed.'}
$metadata=$metadataText | ConvertFrom-Json
$streams=@($metadata.streams)
$renderLog=Get-Content -LiteralPath $log -Encoding UTF8 -Raw
$dimensions=[regex]::Match($renderLog,'encoded-canvas=(\d+)x(\d+)')
if(!$dimensions.Success){throw 'Producer did not report its encoded resolution.'}
if($streams.Count -ne 1 -or $streams[0].codec_type -ne 'video' -or $streams[0].codec_name -ne 'h264' -or $streams[0].pix_fmt -notin @('yuv420p','yuvj420p') -or $streams[0].color_range -ne 'pc' -or $streams[0].width -ne [int]$dimensions.Groups[1].Value -or $streams[0].height -ne [int]$dimensions.Groups[2].Value -or $streams[0].r_frame_rate -ne '60/1' -or [int]$streams[0].nb_frames -ne 324){throw 'Film does not match the local-resolution/60fps/full-range/no-audio contract.'}
if([Math]::Abs([double]$metadata.format.duration-5.4) -gt .02){throw 'Unexpected film duration.'}
$metadataText | Set-Content -LiteralPath (Join-Path $build 'ffprobe.json') -Encoding UTF8
Get-Content -LiteralPath $log -Encoding UTF8
[pscustomobject]@{film=$film;poster=$poster;endFrame=$end;width=$streams[0].width;height=$streams[0].height;frames=[int]$streams[0].nb_frames;fps=$streams[0].r_frame_rate;duration=[double]$metadata.format.duration;level=$streams[0].level;bytes=[long]$metadata.format.size} | ConvertTo-Json
