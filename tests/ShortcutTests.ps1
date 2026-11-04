$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture=Join-Path $projectRoot ('build/shortcut-tests-'+[Guid]::NewGuid().ToString('N'))
$release=Join-Path $fixture 'release'
$releaseScripts=Join-Path $release 'scripts'
$destination=Join-Path $fixture 'destination'
[IO.Directory]::CreateDirectory($releaseScripts) | Out-Null
[IO.Directory]::CreateDirectory($destination) | Out-Null
$installer=Join-Path $releaseScripts 'install-shortcut.ps1'
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/install-shortcut.ps1') -Destination $installer
$fakeExe=Join-Path $release 'CrystalDragon.exe'
[IO.File]::WriteAllText($fakeExe,'Not executable: shortcut fixture only.')
$otherExe=Join-Path $release 'other.exe'
[IO.File]::WriteAllText($otherExe,'Not executable: unowned shortcut target fixture.')
$shell=New-Object -ComObject WScript.Shell
$officialPath=Join-Path $destination 'Codex.lnk'
$official=$shell.CreateShortcut($officialPath)
$official.TargetPath=$otherExe
$official.Description='Unrelated official-looking fixture'
$official.Save()
$officialHash=(Get-FileHash -LiteralPath $officialPath -Algorithm SHA256).Hash
& $installer -DestinationDirectory $destination
$shortcutPath=Join-Path $destination 'Codex Reasoning Slider.lnk'
$shortcut=$shell.CreateShortcut($shortcutPath)
if($shortcut.TargetPath -ne $fakeExe -or $shortcut.Arguments -ne '--cold-test' -or $shortcut.WorkingDirectory -ne $release) {throw 'Release shortcut properties did not match.'}
if([LinkIdentity]::Get($shortcutPath) -ne 'OpenAI.Codex_2p2nqsd0c76g0!App') {throw 'Shortcut does not carry the client identity for taskbar grouping.'}
if([LinkIdentity]::Get($officialPath)) {throw 'Unrelated link gained a client identity.'}
# An owned shortcut may be updated, without scanning other links.
$shortcut.TargetPath=$otherExe;$shortcut.Save()
& $installer -DestinationDirectory $destination
$shortcut=$shell.CreateShortcut($shortcutPath)
if($shortcut.TargetPath -ne $fakeExe) {throw 'Owned shortcut was not updated.'}
if([LinkIdentity]::Get($shortcutPath) -ne 'OpenAI.Codex_2p2nqsd0c76g0!App') {throw 'Owned update lost the client identity.'}
# A collision with an unowned link must remain byte-for-byte untouched.
$shortcut.Description='A different launcher';$shortcut.TargetPath=$otherExe;$shortcut.Save()
$unownedHash=(Get-FileHash -LiteralPath $shortcutPath -Algorithm SHA256).Hash
$refused=$false
try {& $installer -DestinationDirectory $destination} catch {$refused=$true}
if(!$refused -or (Get-FileHash -LiteralPath $shortcutPath -Algorithm SHA256).Hash -ne $unownedHash) {throw 'Unowned shortcut collision was not preserved.'}
if((Get-FileHash -LiteralPath $officialPath -Algorithm SHA256).Hash -ne $officialHash) {throw 'Unrelated official-looking link was changed.'}
# The same installer must also work in a source checkout's build layout.
$source=Join-Path $fixture 'source'
[IO.Directory]::CreateDirectory((Join-Path $source 'scripts')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $source 'build/CrystalDragon')) | Out-Null
$sourceInstaller=Join-Path $source 'scripts/install-shortcut.ps1'
Copy-Item -LiteralPath $installer -Destination $sourceInstaller
$builtExe=Join-Path $source 'build/CrystalDragon/CrystalDragon.exe'
[IO.File]::WriteAllText($builtExe,'Not executable: source build fixture only.')
$sourceDestination=Join-Path $fixture 'source-destination'
& $sourceInstaller -DestinationDirectory $sourceDestination
$sourceLink=$shell.CreateShortcut((Join-Path $sourceDestination 'Codex Reasoning Slider.lnk'))
if($sourceLink.TargetPath -ne $builtExe -or $sourceLink.Arguments -ne '--cold-test') {throw 'Source build shortcut properties did not match.'}
Write-Output 'Shortcut tests passed: release/source targets, owned update and unowned byte preservation. No program or real Desktop shortcut was launched or changed.'
