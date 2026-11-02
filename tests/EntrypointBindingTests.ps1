$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture=Join-Path $root ('build/entrypoint-binding-tests-'+[Guid]::NewGuid().ToString('N'))
$desktop=Join-Path $fixture 'Desktop';$programs=Join-Path $fixture 'Programs';$state=Join-Path $fixture 'state'
foreach($directory in @($desktop,$programs,(Join-Path $programs 'nested'),$state)){[IO.Directory]::CreateDirectory($directory)|Out-Null}
$launcher=Join-Path $fixture 'launcher.exe';$updated=Join-Path $fixture 'updated-launcher.exe';$official=Join-Path $fixture 'official.exe';$alias=Join-Path $fixture 'alias.exe';$other=Join-Path $fixture 'other.exe'
foreach($file in @($launcher,$updated,$official,$alias,$other)){[IO.File]::WriteAllText($file,'Fixture only; never executed.')}
$shell=New-Object -ComObject WScript.Shell
function Link([string]$path,[string]$target,[string]$arguments=''){
    $link=$shell.CreateShortcut($path);$link.TargetPath=$target;$link.Arguments=$arguments;$link.WorkingDirectory=$fixture;$link.Description='Original fixture';$link.IconLocation=$target+',0';$link.Save()
}
function Hash([string]$path){return(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Assert([bool]$value,[string]$message){if(!$value){throw $message}}
$exeLink=Join-Path $desktop 'Codex.lnk';$aliasLink=Join-Path $programs 'nested/ChatGPT.lnk';$shellLink=Join-Path $programs 'Codex Store.lnk'
Link $exeLink $official;Link $aliasLink $alias
$explorer=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)) 'explorer.exe'
Assert (Test-Path -LiteralPath $explorer -PathType Leaf) 'The fixture must use the real Windows Explorer path.'
Link $shellLink $explorer 'shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App'
$uriLink=Join-Path $desktop 'Codex URI.lnk';$projectLink=Join-Path $desktop 'Codex Project.lnk';$wrongLink=Join-Path $programs 'ChatGPT Other.lnk';$unrelated=Join-Path $desktop 'Unrelated.lnk'
Link $uriLink $official 'codex://fixture';Link $projectLink $official '--project fixture';Link $wrongLink $other;Link $unrelated $official
$original=@{};foreach($path in @($exeLink,$aliasLink,$shellLink,$uriLink,$projectLink,$wrongLink,$unrelated)){$original[$path]=Hash $path}
$script=Join-Path $root 'scripts/manage-entrypoints.ps1'
function Run([string]$action,[string]$target=$launcher){
    $output=& $script -Action $action -LauncherPath $target -OfficialExecutable $official -OfficialAlias $alias -StateDirectory $state -DesktopDirectory $desktop -ProgramsDirectory $programs
    return($output|ConvertFrom-Json)
}
$result=Run Bind
Assert ($result.changed -eq 3-and$result.failed -eq 0) ('Only the three basic official links should bind: '+($result|ConvertTo-Json -Compress))
foreach($path in @($exeLink,$aliasLink,$shellLink)){$link=$shell.CreateShortcut($path);Assert ($link.TargetPath -eq $launcher-and$link.Arguments -eq '--cold-test') 'Bound target or arguments incorrect.'}
foreach($path in @($uriLink,$projectLink,$wrongLink,$unrelated)){Assert ((Hash $path)-eq$original[$path]) 'An argument-bearing, foreign or unrelated link changed.'}
$result=Run Bind
Assert ($result.changed -eq 0-and$result.failed -eq 0) 'An already bound installation should be idempotent.'
$result=Run Bind $updated
Assert ($result.changed -eq 3-and$result.failed -eq 0) 'Owned bindings should update to a new launcher.'
Assert ($shell.CreateShortcut($exeLink).TargetPath -eq $updated) 'Updated target missing.'
$result=Run Restore
Assert ($result.changed -eq 3-and$result.failed -eq 0) 'All three originals should restore.'
foreach($path in @($exeLink,$aliasLink,$shellLink)){Assert ((Hash $path)-eq$original[$path]) 'Restored original was not byte-identical.'}
$result=Run Bind
Assert ($result.changed -eq 3-and$result.failed -eq 0) 'Restored originals should bind again.'
Link $exeLink $other '--custom fixture';$externalHash=Hash $exeLink
Remove-Item -LiteralPath $aliasLink
$result=Run Restore
Assert ($result.changed -eq 1-and$result.failed -eq 0) 'Restore should preserve external edits and deletion.'
Assert ((Hash $exeLink)-eq$externalHash) 'An externally edited link was overwritten.'
Assert (!(Test-Path -LiteralPath $aliasLink)) 'A deleted link was recreated.'
$result=Run Bind
Assert ($result.changed -eq 1-and$result.failed -eq 0) 'Only safely restored official links may rebind.'
Assert ((Hash $exeLink)-eq$externalHash) 'An unowned collision was replaced.'

# A recorded path may later be redirected through a directory junction. Even an
# otherwise identical installed link must not let restore write outside its roots.
$parent=Join-Path $programs 'redirected';$outside=Join-Path $fixture 'outside'
[IO.Directory]::CreateDirectory($parent)|Out-Null;[IO.Directory]::CreateDirectory($outside)|Out-Null
$parentLink=Join-Path $parent 'Codex Parent.lnk';Link $parentLink $official
$result=Run Bind
Assert ($result.changed -eq 1-and$result.failed -eq 0) 'The extra basic link should bind before its parent is redirected.'
$outsideLink=Join-Path $outside 'Codex Parent.lnk';Copy-Item -LiteralPath $parentLink -Destination $outsideLink
$outsideHash=Hash $outsideLink
foreach($path in @($parent,$outside,$parentLink,$outsideLink)){
    Assert ([IO.Path]::GetFullPath($path).StartsWith([IO.Path]::GetFullPath($fixture)+'\',[StringComparison]::OrdinalIgnoreCase)) 'Junction fixture escaped the test workspace.'
}
Remove-Item -LiteralPath $parentLink
[IO.Directory]::Delete($parent,$false)
New-Item -ItemType Junction -Path $parent -Target $outside|Out-Null
Assert (((Get-Item -LiteralPath $parent).Attributes-band[IO.FileAttributes]::ReparsePoint)-ne 0) 'Junction fixture was not created.'
$result=Run Restore
Assert ($result.failed -eq 0-and$result.skipped -ge 1) 'Reparse-parent restore should skip safely without blocking unrelated records.'
Assert ((Hash $outsideLink)-eq$outsideHash) 'Restore followed a redirected parent outside the allowed roots.'

# A backed-up link whose replacement cannot be created must not poison the state
# later saved for another successful link. Only this fixture directory gets a deny ACE.
$denied=Join-Path $fixture 'partial-denied';$writable=Join-Path $fixture 'partial-writable';$partialState=Join-Path $fixture 'partial-state'
foreach($directory in @($denied,$writable,$partialState)){[IO.Directory]::CreateDirectory($directory)|Out-Null}
$failedLink=Join-Path $denied 'Codex Fails.lnk';$okLink=Join-Path $writable 'Codex Works.lnk'
Link $failedLink $official;Link $okLink $official
$failedOriginal=Hash $failedLink;$okOriginal=Hash $okLink
$oldAcl=Get-Acl -LiteralPath $denied
$restricted=Get-Acl -LiteralPath $denied
$deny=[Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.WindowsIdentity]::GetCurrent().User,[Security.AccessControl.FileSystemRights]::CreateFiles,[Security.AccessControl.InheritanceFlags]::None,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Deny)
$restricted.AddAccessRule($deny)
try{
    Set-Acl -LiteralPath $denied -AclObject $restricted
    $deniedWrite=$false
    try{[IO.File]::WriteAllText((Join-Path $denied 'write-probe.tmp'),'fixture')}catch [UnauthorizedAccessException]{$deniedWrite=$true}
    Assert $deniedWrite 'The partial-failure fixture must actually deny replacement-file creation.'
    $result=& $script -Action Bind -LauncherPath $launcher -OfficialExecutable $official -OfficialAlias $alias -StateDirectory $partialState -DesktopDirectory $denied -ProgramsDirectory $writable|ConvertFrom-Json
    Assert ($result.changed -eq 1-and$result.failed -eq 1) 'One candidate should fail before replacement while the later candidate still binds.'
    $partial=Get-Content -LiteralPath (Join-Path $partialState 'bindings.json') -Encoding UTF8 -Raw|ConvertFrom-Json
    Assert (@($partial.records).Count -eq 1-and$partial.records[0].installedHash -match '^[0-9A-Fa-f]{64}$') 'Failed pre-replacement candidate polluted later recovery state.'
    $result=& $script -Action Restore -LauncherPath $launcher -StateDirectory $partialState -DesktopDirectory $denied -ProgramsDirectory $writable|ConvertFrom-Json
    Assert ($result.changed -eq 1-and$result.failed -eq 0) 'A prior candidate failure blocked restoration of the successful candidate.'
    Assert ((Hash $failedLink)-eq$failedOriginal-and(Hash $okLink)-eq$okOriginal) 'Partial-failure restore did not preserve both originals.'
}finally{Set-Acl -LiteralPath $denied -AclObject $oldAcl}

# An overlapping bind/restore must fail before reading or mutating shared state.
$stateHash=Hash (Join-Path $partialState 'bindings.json')
$held=[IO.File]::Open((Join-Path $partialState 'bindings.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{
    foreach($action in @('Bind','Restore')){
        $result=& $script -Action $action -LauncherPath $launcher -OfficialExecutable $official -OfficialAlias $alias -StateDirectory $partialState -DesktopDirectory $denied -ProgramsDirectory $writable|ConvertFrom-Json
        Assert ($result.changed -eq 0-and$result.failed -eq 1) 'An overlapping operation did not report busy without changing links.'
        Assert ((Hash (Join-Path $partialState 'bindings.json'))-eq$stateHash) 'An overlapping operation changed recovery records.'
        Assert ((Hash $failedLink)-eq$failedOriginal-and(Hash $okLink)-eq$okOriginal) 'An overlapping operation changed shortcuts.'
    }
}finally{$held.Dispose()}
$result=& $script -Action Restore -LauncherPath $launcher -StateDirectory $partialState -DesktopDirectory $denied -ProgramsDirectory $writable|ConvertFrom-Json
Assert ($result.failed -eq 0) 'Recovery state was damaged by overlapping operations.'
Write-Output 'Entrypoint binding: exact targets, safe skips, owned upgrade/restore, external edits, junction parents, partial-failure recovery and exclusive state locking passed. Isolated fixtures only.'
