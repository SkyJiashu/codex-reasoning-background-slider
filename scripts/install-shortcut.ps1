[CmdletBinding()]
param(
    [string]$DestinationDirectory=[Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$executable=Join-Path $projectRoot 'CrystalDragon.exe'
if(!(Test-Path -LiteralPath $executable -PathType Leaf)) {
    $executable=Join-Path $projectRoot 'build/CrystalDragon/CrystalDragon.exe'
}
if(!(Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'CrystalDragon.exe was not found. Extract the complete release package, or build the source project first.'
}
if([string]::IsNullOrWhiteSpace($DestinationDirectory)) {throw 'A shortcut destination is required.'}
$destination=[IO.Path]::GetFullPath($DestinationDirectory)
$shortcutPath=Join-Path $destination 'Codex - Crystal Dragon.lnk'
$marker='Codex Crystal Dragon launcher (codex-skin-control)'
$shell=New-Object -ComObject WScript.Shell
if(Test-Path -LiteralPath $shortcutPath) {
    if(!(Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {throw 'The shortcut name is already occupied. No shortcut was changed.'}
    $existing=$shell.CreateShortcut($shortcutPath)
    if($existing.Description -ne $marker) {
        throw 'A different shortcut already uses Codex - Crystal Dragon. Rename it before installing this independent launcher. Nothing was overwritten.'
    }
}
[IO.Directory]::CreateDirectory($destination) | Out-Null
$shortcut=$shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath=$executable
$shortcut.Arguments='--cold-test'
$shortcut.WorkingDirectory=Split-Path -Parent $executable
$shortcut.IconLocation=$executable+',0'
$shortcut.Description=$marker
$shortcut.Save()
Write-Output ('Independent launcher shortcut saved: '+$shortcutPath)
