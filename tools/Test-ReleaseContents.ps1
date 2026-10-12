param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$publishRoot = [IO.Path]::GetFullPath($Directory).TrimEnd([char[]]'\/') + [IO.Path]::DirectorySeparatorChar
$runtimeFiles = @(Get-ChildItem -LiteralPath $Directory -Recurse -File | Where-Object {
    $relativePath = $_.FullName.Substring($publishRoot.Length)
    $privateDirectory = $relativePath -match '(?i)(^|[\\/])(Data|passwords|accounts|captures)[\\/]|\.exe\.WebView2[\\/]'
    $databaseFile = $_.Name -match '(?i)\.(db|sqlite|sqlite3)(-(wal|shm|journal))?$'
    $privateDirectory -or $databaseFile
})
if ($runtimeFiles.Count -gt 0) { throw 'Publish folder contains runtime data. Refusing to package it.' }
