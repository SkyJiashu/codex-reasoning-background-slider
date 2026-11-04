[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Bind','Restore')][string]$Action,
    [Parameter(Mandatory=$true)][string]$LauncherPath,
    [string]$OfficialExecutable='',
    [string]$OfficialAlias='',
    [Parameter(Mandatory=$true)][string]$StateDirectory,
    [string]$DesktopDirectory=[Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory),
    [string]$ProgramsDirectory=[Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
)
$ErrorActionPreference='Stop'
$summary=[ordered]@{changed=0;skipped=0;failed=0}
$records=New-Object 'System.Collections.Generic.List[object]'
$stateLock=$null

function SamePath([string]$a,[string]$b) {
    if([string]::IsNullOrWhiteSpace($a)-or [string]::IsNullOrWhiteSpace($b)){return $false}
    try{return [string]::Equals([IO.Path]::GetFullPath($a),[IO.Path]::GetFullPath($b),[StringComparison]::OrdinalIgnoreCase)}catch{return $false}
}
function AllowedPath([string]$path,[bool]$checkParents=$true) {
    try{
        $full=[IO.Path]::GetFullPath($path)
        if([IO.Path]::GetExtension($full) -ine '.lnk'){return $false}
        foreach($root in $script:roots){
            if(!$full.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){continue}
            if(!$checkParents){return $true}
            $directory=[IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($full))
            $rootDirectory=$root.TrimEnd([IO.Path]::DirectorySeparatorChar)
            while($directory){
                if(!$directory.Exists-or($directory.Attributes-band[IO.FileAttributes]::ReparsePoint)-ne 0){return $false}
                if(SamePath $directory.FullName $rootDirectory){return $true}
                $directory=$directory.Parent
            }
        }
    }catch{}
    return $false
}
function Hash([string]$path) {return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function SaveState {
    $data=[ordered]@{version=1;records=@($script:records.ToArray())}
    $temp=$script:statePath+'.next'
    [IO.File]::WriteAllText($temp,($data|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    if([IO.File]::Exists($script:statePath)){[IO.File]::Replace($temp,$script:statePath,[NullString]::Value)}else{[IO.File]::Move($temp,$script:statePath)}
}
function BasicOfficial($link) {
    $target=[string]$link.TargetPath
    $direct=(SamePath $target $script:OfficialExecutable)-or(SamePath $target $script:OfficialAlias)
    $shellTarget=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)) 'explorer.exe'
    $explorer=SamePath $target $shellTarget
    if(!$direct-and!$explorer){return $false}
    $arguments=[string]$link.Arguments
    if($direct){return [string]::IsNullOrWhiteSpace($arguments)}
    $app='shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App'
    return $arguments -ieq $app -or $arguments -ieq ('"'+$app+'"')
}
function CandidateFiles {
    $found=New-Object 'System.Collections.Generic.List[string]'
    foreach($root in @($script:DesktopDirectory,$script:ProgramsDirectory)){
        if([string]::IsNullOrWhiteSpace($root)-or !(Test-Path -LiteralPath $root -PathType Container)){continue}
        $stack=New-Object 'System.Collections.Generic.Stack[string]';$stack.Push([IO.Path]::GetFullPath($root))
        while($stack.Count -gt 0){
            $directory=$stack.Pop()
            try{$items=@(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)}catch{$script:summary.failed++;continue}
            foreach($item in $items){
                if(($item.Attributes-band[IO.FileAttributes]::ReparsePoint)-ne 0){continue}
                if($item.PSIsContainer){if(SamePath $root $script:ProgramsDirectory){$stack.Push($item.FullName)};continue}
                if($item.Extension -ieq '.lnk'-and($item.BaseName -ilike 'Codex*'-or$item.BaseName -ilike 'ChatGPT*')-and(AllowedPath $item.FullName)){$found.Add($item.FullName)}
            }
        }
    }
    return @($found.ToArray()|Select-Object -Unique)
}
try{
    $LauncherPath=[IO.Path]::GetFullPath($LauncherPath)
    $StateDirectory=[IO.Path]::GetFullPath($StateDirectory)
    $roots=@($DesktopDirectory,$ProgramsDirectory)|Where-Object{![string]::IsNullOrWhiteSpace($_)}|ForEach-Object{[IO.Path]::GetFullPath($_).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar}|Select-Object -Unique
    $statePath=Join-Path $StateDirectory 'bindings.json'
    [IO.Directory]::CreateDirectory($StateDirectory)|Out-Null
    $stateLock=[IO.File]::Open((Join-Path $StateDirectory 'bindings.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    if([IO.File]::Exists($statePath)){
        $saved=Get-Content -LiteralPath $statePath -Encoding UTF8 -Raw|ConvertFrom-Json
        if($saved.version -ne 1){throw 'Unsupported entrypoint state'}
        foreach($record in @($saved.records)){
            if(!$record-or!(AllowedPath ([string]$record.path) $false)-or[string]$record.backup -notmatch '^[0-9a-f]{32}\.lnk$'-or[string]$record.originalHash -notmatch '^[0-9a-fA-F]{64}$'-or[string]$record.installedHash -notmatch '^[0-9a-fA-F]{64}$'){throw 'Invalid entrypoint state'}
            $records.Add($record)
        }
    }
    $shell=New-Object -ComObject WScript.Shell
    if($Action -eq 'Bind'){
        if(!(Test-Path -LiteralPath $LauncherPath -PathType Leaf)-or(!(Test-Path -LiteralPath $OfficialExecutable -PathType Leaf)-and!(Test-Path -LiteralPath $OfficialAlias -PathType Leaf))){throw 'Missing launcher or verified official target'}
        foreach($path in @(CandidateFiles)){
            $temp=$null;$newRecord=$false
            try{
                $record=@($records|Where-Object{SamePath ([string]$_.path) $path})|Select-Object -First 1
                $currentHash=Hash $path
                $link=$shell.CreateShortcut($path)
                if($record){
                    $backup=Join-Path $StateDirectory ([string]$record.backup)
                    if(!(Test-Path -LiteralPath $backup -PathType Leaf)-or(Hash $backup)-ine[string]$record.originalHash){$summary.skipped++;continue}
                    if($currentHash -ine [string]$record.installedHash -and $currentHash -ine [string]$record.originalHash){$summary.skipped++;continue}
                    if($currentHash -ieq [string]$record.installedHash){
                        if((SamePath ([string]$link.TargetPath) $LauncherPath)-and$link.Arguments -eq '--cold-test'-and(SamePath ([string]$link.WorkingDirectory) (Split-Path -Parent $LauncherPath))){$summary.skipped++;continue}
                    }elseif(!(BasicOfficial $link)){$summary.skipped++;continue}
                }elseif(!(BasicOfficial $link)){$summary.skipped++;continue}
                if(!$record){
                    $backupName=[Guid]::NewGuid().ToString('N')+'.lnk'
                    [IO.File]::Copy($path,(Join-Path $StateDirectory $backupName),$false)
                    $record=[pscustomobject]@{path=$path;backup=$backupName;originalHash=$currentHash;installedHash=''}
                    $newRecord=$true
                }
                $temp=Join-Path (Split-Path -Parent $path) ([Guid]::NewGuid().ToString('N')+'.lnk')
                $replacement=$shell.CreateShortcut($temp)
                $replacement.TargetPath=$LauncherPath;$replacement.Arguments='--cold-test';$replacement.WorkingDirectory=Split-Path -Parent $LauncherPath
                $replacement.IconLocation=$LauncherPath+',0' # A bound link must look different from the original it replaced.
                $replacement.Description='Codex Reasoning Background Slider launch binding'
                $replacement.Save()
                $record.installedHash=Hash $temp
                if($newRecord){$records.Add($record)}
                SaveState # Write the recovery record before changing the original link.
                [IO.File]::Replace($temp,$path,[NullString]::Value);$temp=$null
                $summary.changed++
            }catch{$summary.failed++}finally{if($temp-and[IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
        }
    }else{
        foreach($record in $records){
            $temp=$null
            try{
                $path=[string]$record.path;$backup=Join-Path $StateDirectory ([string]$record.backup)
                if(!(AllowedPath $path)-or!(Test-Path -LiteralPath $path -PathType Leaf)-or((Get-Item -LiteralPath $path).Attributes-band[IO.FileAttributes]::ReparsePoint)-ne 0-or!(Test-Path -LiteralPath $backup -PathType Leaf)){$summary.skipped++;continue}
                if((Hash $path)-ine[string]$record.installedHash-or(Hash $backup)-ine[string]$record.originalHash){$summary.skipped++;continue}
                $temp=Join-Path (Split-Path -Parent $path) ([Guid]::NewGuid().ToString('N')+'.lnk')
                [IO.File]::Copy($backup,$temp,$false);[IO.File]::Replace($temp,$path,[NullString]::Value);$temp=$null
                $summary.changed++
            }catch{$summary.failed++}finally{if($temp-and[IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
        }
    }
}catch{$summary.failed++}finally{if($stateLock){$stateLock.Dispose()}}
$summary|ConvertTo-Json -Compress
