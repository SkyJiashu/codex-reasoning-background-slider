$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$entries=@('README.md','LICENSE','NOTICE','THIRD_PARTY_NOTICES.md','.gitignore','.gitattributes','Preview.cmd','Start-Cold-Test.cmd','Restore.cmd','Install-Shortcut.cmd','licenses/dragon-codex-boot-MIT.txt','tools/RenderFilm.cs','docs/architecture.md')
foreach($folder in @('src','tests')){
    $entries+=@(Get-ChildItem -LiteralPath (Join-Path $root $folder) -File | Where-Object {$_.Extension -in @('.cs','.js','.cjs','.css','.manifest','.ps1')} | ForEach-Object {($_.FullName.Substring($root.Length+1)).Replace('\','/')})
}
foreach($name in @('build.ps1','test.ps1','package.ps1','package-source.ps1','render-film.ps1','install-shortcut.ps1')){$entries+='scripts/'+$name}
$entries+='assets/README.md'
# Public source archives intentionally omit user-supplied artwork.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Directory]::CreateDirectory((Join-Path $root 'build')) | Out-Null
$path=Join-Path $root 'build/CrystalDragon-source-0.2.13.zip'
$stream=[IO.File]::Open($path,[IO.FileMode]::Create,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{
    $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try{foreach($entry in $entries){
        if($entry -match '(?i)\.mp4$|(^|/)(build|installed|analysis|cache)/|startup-(poster|end)\.png$'){throw 'Generated/private artifact in source list'}
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $root $entry),('CrystalDragon-source/'+$entry),[IO.Compression.CompressionLevel]::Optimal)|Out-Null
    }}finally{$zip.Dispose()}
}finally{$stream.Dispose()}
[pscustomobject]@{sourceArchive=$path;files=$entries.Count;prebuiltMovies=$false;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}|ConvertTo-Json
