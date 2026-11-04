[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$requiredArt=@('assets/stages-v10-review/01_Light.png','assets/stages-v11-nose/medium.png','assets/stages-v11-nose/high.png','assets/stages-v11-nose/extra-high.png','assets/stages-v11-nose/ultra.png')
foreach($relative in $requiredArt){if(!(Test-Path -LiteralPath (Join-Path $projectRoot $relative) -PathType Leaf)){throw 'A bundled character PNG is missing. Restore the complete source archive; see assets/README.md.'}}
$outputRoot = Join-Path $projectRoot 'build/CrystalDragon'
$compiler = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)) 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) { throw '.NET Framework x64 compiler unavailable.' }
$active = @(Get-Process -Name 'CrystalDragon' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $outputRoot 'CrystalDragon.exe') })
if ($active.Count -gt 0) { throw 'Close the offline preview or wait for the cold-test helper to exit before rebuilding.' }
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
foreach ($folder in @('assets','scripts','licenses')) { [IO.Directory]::CreateDirectory((Join-Path $outputRoot $folder)) | Out-Null }
$frameworkRoot = Split-Path -Parent $compiler
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll')
foreach ($name in @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')) { $references += Join-Path $frameworkRoot ('WPF/' + $name) }
$compileArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/warnaserror+',('/win32manifest:' + (Join-Path $projectRoot 'src/app.manifest')),('/win32icon:' + (Join-Path $projectRoot 'assets/app-icon.ico')),('/out:' + (Join-Path $outputRoot 'CrystalDragon.exe')))
foreach ($reference in $references) { $compileArgs += '/reference:' + $reference }
$compileArgs += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
$renderArgs=@('/nologo','/target:exe','/platform:x64','/optimize+','/warnaserror+',('/win32manifest:'+(Join-Path $projectRoot 'src/app.manifest')),('/out:'+(Join-Path $outputRoot 'RenderFilm.exe')))
foreach($reference in $references){$renderArgs+='/reference:'+$reference}
$renderArgs+=@((Join-Path $projectRoot 'src/DataScene.cs'),(Join-Path $projectRoot 'tools/RenderFilm.cs'))
& $compiler @renderArgs
if($LASTEXITCODE -ne 0){throw 'Local cache renderer compilation failed.'}
$fingerprintText=[IO.File]::ReadAllText((Join-Path $projectRoot 'src/DataScene.cs'))+[IO.File]::ReadAllText((Join-Path $projectRoot 'tools/RenderFilm.cs'))
$sha=[Security.Cryptography.SHA256]::Create()
try{$fingerprint=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($fingerprintText)))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
[IO.File]::WriteAllText((Join-Path $outputRoot 'animation-source.txt'),$fingerprint,[Text.Encoding]::ASCII)
$art = @{'01_Light.png'='assets/stages-v10-review/01_Light.png';'medium.png'='assets/stages-v11-nose/medium.png';'high.png'='assets/stages-v11-nose/high.png';'extra-high.png'='assets/stages-v11-nose/extra-high.png';'ultra.png'='assets/stages-v11-nose/ultra.png'}
foreach ($name in $art.Keys) {
    $sourceArt = Join-Path $projectRoot $art[$name]
    $outputArt = Join-Path $outputRoot ('assets/' + $name)
    Copy-Item -LiteralPath $sourceArt -Destination $outputArt -Force
    if ((Get-FileHash -LiteralPath $sourceArt).Hash -ne (Get-FileHash -LiteralPath $outputArt).Hash) { throw 'Approved art copy mismatch.' }
}
foreach ($name in @('skin.js','skin.css','preload.js')) { Copy-Item -LiteralPath (Join-Path $projectRoot ('src/' + $name)) -Destination (Join-Path $outputRoot $name) -Force }
foreach ($name in @('README.md','README.zh-CN.md','README.en.md','LICENSE','NOTICE','ARTWORK_LICENSE.md','THIRD_PARTY_NOTICES.md','Preview.cmd','Start-Cold-Test.cmd','Restore.cmd','Install-Shortcut.cmd','Manage-Entrypoints.cmd')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination (Join-Path $outputRoot $name) -Force }
[IO.Directory]::CreateDirectory((Join-Path $outputRoot 'docs'))|Out-Null
foreach($name in @('install-agent.zh-CN.md','install-agent.en.md')) {Copy-Item -LiteralPath (Join-Path $projectRoot ('docs/'+$name)) -Destination (Join-Path $outputRoot ('docs/'+$name)) -Force}
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets/README.md') -Destination (Join-Path $outputRoot 'assets/README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/install-shortcut.ps1') -Destination (Join-Path $outputRoot 'scripts/install-shortcut.ps1') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/manage-entrypoints.ps1') -Destination (Join-Path $outputRoot 'scripts/manage-entrypoints.ps1') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/check-client-compat.cjs') -Destination (Join-Path $outputRoot 'scripts/check-client-compat.cjs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses/dragon-codex-boot-MIT.txt') -Destination (Join-Path $outputRoot 'licenses/dragon-codex-boot-MIT.txt') -Force
Write-Output ('Built offline candidate: ' + (Join-Path $outputRoot 'CrystalDragon.exe'))
