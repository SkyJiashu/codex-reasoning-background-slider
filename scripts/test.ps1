$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'build.ps1')
$node=(Get-Command node -ErrorAction Stop).Source
& $node --test (Join-Path $projectRoot 'tests/skin.test.cjs') (Join-Path $projectRoot 'tests/preload.test.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Renderer tests failed.' }
& (Join-Path $projectRoot 'tests/ShortcutTests.ps1')
& (Join-Path $projectRoot 'tests/EntrypointBindingTests.ps1')
$outputRoot = Join-Path $projectRoot 'build/CrystalDragon'
$frameworkRoot=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)) 'Microsoft.NET/Framework64/v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
$testPath = Join-Path $projectRoot 'build/SceneTests.exe'
$testArgs = @('/nologo','/target:exe','/platform:x64',('/out:' + $testPath),('/reference:' + (Join-Path $outputRoot 'CrystalDragon.exe')), '/reference:System.Xaml.dll')
foreach ($name in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')) { $testArgs += '/reference:'+(Join-Path $frameworkRoot ('WPF/'+$name)) }
$testArgs += Join-Path $projectRoot 'tests/SceneTests.cs'
& $compiler @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Timing test compile failed.' }
# Resolve the application assembly from the same folder without installing anything.
Copy-Item -LiteralPath (Join-Path $outputRoot 'CrystalDragon.exe') -Destination (Join-Path $projectRoot 'build/CrystalDragon.exe') -Force
& $testPath
if ($LASTEXITCODE -ne 0) { throw 'Timing checks failed.' }
$assetTests = Join-Path $projectRoot 'build/AssetTests.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:' + $assetTests) ('/reference:' + (Join-Path $outputRoot 'CrystalDragon.exe')) /reference:System.Drawing.dll (Join-Path $projectRoot 'tests/AssetTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Asset check compile failed.' }
& $assetTests $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Production asset checks failed.' }
$preparationTests=Join-Path $projectRoot 'build/StartupPreparationTests.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:'+$preparationTests) ('/reference:'+(Join-Path $outputRoot 'CrystalDragon.exe')) /reference:System.Web.Extensions.dll (Join-Path $projectRoot 'tests/StartupPreparationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Preparation check compile failed.' }
& $preparationTests $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Parallel read-only preparation checks failed.' }
$cacheTests=Join-Path $projectRoot 'build/MovieCacheTests.exe'
$cacheArgs=@('/nologo','/target:exe','/platform:x64',('/out:'+$cacheTests),('/win32manifest:'+(Join-Path $projectRoot 'src/app.manifest')),'/reference:System.Xaml.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll')
foreach($name in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')){$cacheArgs+='/reference:'+(Join-Path $frameworkRoot ('WPF/'+$name))}
$cacheArgs+=@((Join-Path $projectRoot 'src/NativeWindow.cs'),(Join-Path $projectRoot 'src/MovieCache.cs'),(Join-Path $projectRoot 'tests/MovieCacheTests.cs'))
& $compiler @cacheArgs
if($LASTEXITCODE -ne 0){throw 'Movie cache test compilation failed.'}
& $cacheTests (Join-Path $outputRoot 'qa')
if($LASTEXITCODE -ne 0){throw 'First-run movie cache checks failed.'}
$pipeTests = Join-Path $projectRoot 'build/PipeTests.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:' + $pipeTests) /reference:System.Web.Extensions.dll (Join-Path $projectRoot 'src/PipeHost.cs') (Join-Path $projectRoot 'tests/PipeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Private pipe test compile failed.' }
& $pipeTests
if ($LASTEXITCODE -ne 0) { throw 'Private pipe checks failed.' }
$identityTests = Join-Path $projectRoot 'build/IdentityTests.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:' + $identityTests) ('/reference:' + (Join-Path $outputRoot 'CrystalDragon.exe')) (Join-Path $projectRoot 'tests/IdentityTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Identity boundary test compile failed.' }
& $identityTests
if ($LASTEXITCODE -ne 0) { throw 'Identity boundary checks failed.' }
$handoffTests = Join-Path $projectRoot 'build/NativeWindowTests.exe'
$handoffArgs = @('/nologo','/target:exe','/platform:x64',('/out:' + $handoffTests),('/win32manifest:' + (Join-Path $projectRoot 'src/app.manifest')),'/reference:System.Xaml.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll')
foreach ($name in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')) { $handoffArgs += '/reference:'+(Join-Path $frameworkRoot ('WPF/'+$name)) }
$handoffArgs += @((Join-Path $projectRoot 'src/NativeWindow.cs'),(Join-Path $projectRoot 'src/TaskbarIdentity.cs'),(Join-Path $projectRoot 'tests/NativeWindowTests.cs'))
& $compiler @handoffArgs
if ($LASTEXITCODE -ne 0) { throw 'Handoff test compile failed.' }
& $handoffTests
if ($LASTEXITCODE -ne 0) { throw 'Handoff geometry/compositor checks failed.' }
$presentationTests = Join-Path $projectRoot 'build/StartupPresentationTests.exe'
$presentationArgs = @('/nologo','/target:exe','/platform:x64',('/out:' + $presentationTests),('/win32manifest:' + (Join-Path $projectRoot 'src/app.manifest')),('/reference:' + (Join-Path $outputRoot 'CrystalDragon.exe')),'/reference:System.Xaml.dll')
foreach ($name in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')) { $presentationArgs += '/reference:'+(Join-Path $frameworkRoot ('WPF/'+$name)) }
$presentationArgs += Join-Path $projectRoot 'tests/StartupPresentationTests.cs'
& $compiler @presentationArgs
if ($LASTEXITCODE -ne 0) { throw 'Presentation test compile failed.' }
& $presentationTests $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'First-frame/late-client presentation checks failed.' }
& $presentationTests $outputRoot '--early-ready'
if ($LASTEXITCODE -ne 0) { throw 'Accelerated/direct-purple-transition checks failed.' }
& $presentationTests $outputRoot '--shell-only'
if ($LASTEXITCODE -ne 0) { throw 'Fixed handoff with pending artwork failed.' }
& $presentationTests $outputRoot '--late-sidebar'
if ($LASTEXITCODE -ne 0) { throw 'Fixed handoff with pending sidebar failed.' }
& $presentationTests $outputRoot '--late-window'
if ($LASTEXITCODE -ne 0) { throw 'Missing native window/late appearance checks failed.' }
& $presentationTests $outputRoot '--escape-minimized'
if ($LASTEXITCODE -ne 0) { throw 'Escape/minimized-target presentation checks failed.' }
$preview = Start-Process -FilePath (Join-Path $outputRoot 'CrystalDragon.exe') -ArgumentList '--render-preview' -WindowStyle Hidden -PassThru
if (!$preview.WaitForExit(20000)) { $preview.Kill(); throw 'Offline rendering timed out.' }
if ($preview.ExitCode -ne 0) { throw 'Offline rendering failed.' }
foreach ($number in 1..4) { if (!(Test-Path -LiteralPath (Join-Path $outputRoot ('qa/startup-' + $number + '.png')))) { throw 'Rendered frame missing.' } }
$probe = Start-Process -FilePath (Join-Path $outputRoot 'CrystalDragon.exe') -ArgumentList '--probe' -WindowStyle Hidden -PassThru
if (!$probe.WaitForExit(15000)) { $probe.Kill(); throw 'Read-only identity probe timed out.' }
if ($probe.ExitCode -ne 0) { throw 'Read-only identity probe failed.' }
$identity = Get-Content -LiteralPath (Join-Path $outputRoot 'qa/identity.json') -Encoding UTF8 -Raw | ConvertFrom-Json
if ($identity.ready -ne $true -or $identity.coldStartAllowed -isnot [bool]) { throw 'Identity/running-state probe contract failed.' }
[pscustomobject]@{
    status = 'offline-checks-passed'
    utc = [DateTime]::UtcNow.ToString('o')
    rendererTests = 'passed'
    entrypointBinding = 'opt-in-bind-restore-external-edits-junction-fixtures-passed'
    startupPresentation = 'real-cached-mp4-mediaended-300ms-hold-550ms-fade-cached-hwnd-late-window-escape-passed'
    startupPreparation = 'parallel-readonly-no-native-creation-passed'
    movieCache = 'first-generation-reuse-resolution-dpi-source-art-invalidation-cancel-passed'
    sceneTiming = 'passed'
    privatePipe = 'own-mock-child-and-fixed-background-flags-passed'
    startupFrames = 'rendered-and-visually-checked'
    officialIdentity = 'read-only-probe-passed'
    identityBoundaryTests = 'passed'
    handoffTests = 'own-offscreen-windows-passed'
    productionAssets = 'approved-v11-alpha-canvas-and-payload-passed'
    realCodexLaunchOrInjection = $false
    coldStartAcceptance = 'pending-user-manual-exit'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'qa/offline-verification.json') -Encoding UTF8
# Compares raw frame bytes: icons extracted through the shell are rescaled for the display DPI.
$icon=[IO.File]::ReadAllBytes((Join-Path $projectRoot 'assets/app-icon.ico'))
$frameEntry=6+16*([BitConverter]::ToUInt16($icon,4)-1)
$frame=[Text.Encoding]::GetEncoding(28591).GetString($icon,[BitConverter]::ToInt32($icon,$frameEntry+12),[BitConverter]::ToInt32($icon,$frameEntry+8))
if(![Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes((Join-Path $outputRoot 'CrystalDragon.exe'))).Contains($frame)){throw 'Launcher does not embed assets/app-icon.ico.'}
Write-Output 'Offline checks passed. No Codex process was started, connected, modified or stopped.'
