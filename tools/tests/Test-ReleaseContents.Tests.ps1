$ErrorActionPreference = 'Stop'
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../artifacts/release-content-tests/$([Guid]::NewGuid().ToString('N'))/Data/publish"))
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
Set-Content -LiteralPath (Join-Path $fixtureRoot 'Microsoft.Data.Sqlite.dll') -Value 'Synthetic dependency fixture'
Set-Content -LiteralPath (Join-Path $fixtureRoot 'e_sqlite3.dll') -Value 'Synthetic dependency fixture'
$validator = Join-Path $PSScriptRoot '../Test-ReleaseContents.ps1'
& $validator -Directory $fixtureRoot
foreach ($fixtureName in @('gold-manager.db', 'cache.sqlite-wal', 'passwords/example.bin', 'DAMiner.exe.WebView2/Default/Cookies')) {
    $fixtureFile = Join-Path $fixtureRoot $fixtureName
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($fixtureFile)) -Force | Out-Null
    Set-Content -LiteralPath $fixtureFile -Value 'Synthetic runtime fixture'
    $wasRejected = $false
    try { & $validator -Directory $fixtureRoot } catch { $wasRejected = $true }
    if (-not $wasRejected) { throw "Runtime fixture was accepted: $fixtureName" }
    Remove-Item -LiteralPath $fixtureFile
}
Write-Output 'Release content checks passed: SQLite libraries allowed; runtime databases, passwords, and browser caches rejected.'
