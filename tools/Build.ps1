param([switch]$Publish)
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet test tests/DAGoldManager.Tests -c Release -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($Publish) {
        & (Join-Path $PSScriptRoot 'Publish-Release.ps1') -SkipTests
    } else {
        dotnet build src/DAGoldManager.Desktop -c Release -m:1
    }
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
} finally { Pop-Location }
