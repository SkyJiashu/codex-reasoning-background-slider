$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = Join-Path $projectRoot 'build/CrystalDragon'
$report = Get-Content -LiteralPath (Join-Path $outputRoot 'qa/offline-verification.json') -Encoding UTF8 -Raw | ConvertFrom-Json
if ($report.status -ne 'offline-checks-passed' -or $report.realCodexLaunchOrInjection -ne $false) { throw 'Run offline tests before packaging.' }
$entries = @('CrystalDragon.exe','RenderFilm.exe','animation-source.txt','skin.js','skin.css','preload.js','Preview.cmd','Start-Cold-Test.cmd','Restore.cmd','Install-Shortcut.cmd','scripts/install-shortcut.ps1','README.md','LICENSE','NOTICE','ARTWORK_LICENSE.md','assets/README.md','THIRD_PARTY_NOTICES.md','licenses/dragon-codex-boot-MIT.txt')
foreach ($name in @('01_Light.png','medium.png','high.png','extra-high.png','ultra.png')) { $entries += 'assets/' + $name }
if (@($entries | Select-Object -Unique).Count -ne $entries.Count) { throw 'Duplicate package entry.' }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $projectRoot 'build/CrystalDragon-0.2.14-windows-x64.zip'
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($entry in $entries) {
            $source = Join-Path $outputRoot $entry
            if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw ('Missing package file: ' + $entry) }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $source, ('CrystalDragon/' + $entry), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
$check = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($check.Entries.Count -ne $entries.Count -or @($check.Entries | Where-Object { $_.FullName -match 'analysis/|status\.json|identity\.json|\.\.|\.mp4$|startup-(poster|end)\.png$' }).Count -gt 0) { throw 'Package allowlist check failed.' }
} finally { $check.Dispose() }
[pscustomobject]@{ artifact = $zipPath; files = $entries.Count; sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash; liveTest = 'pending' } | ConvertTo-Json
