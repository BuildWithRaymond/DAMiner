param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.2',
    [ValidatePattern('^$|^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?/[A-Za-z0-9_.-]{1,100}$')][string]$Repository = 'BuildWithRaymond/DAMiner',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$releaseWorkspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# Always package a fresh folder, before any app launch can add runtime caches.
$releasePublish = Join-Path $releaseWorkspace "artifacts/release-build/$Version-$([Guid]::NewGuid().ToString('N'))/publish"
$releaseOutput = Join-Path $releaseWorkspace 'artifacts/releases'
Push-Location $releaseWorkspace
try {
    if (-not $SkipTests) {
        dotnet test tests/DAGoldManager.Tests -c Release "-p:Version=$Version" -m:1 /nr:false
        if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
        & (Join-Path $PSScriptRoot 'tests/Test-ReleaseContents.Tests.ps1')
    }
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Packaging tool restore failed.' }
    dotnet publish src/DAGoldManager.Desktop -c Release -r win-x64 --self-contained true "-p:Version=$Version" "-p:UpdateRepository=$Repository" -o $releasePublish -m:1 /nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    & (Join-Path $PSScriptRoot 'Test-ReleaseContents.ps1') -Directory $releasePublish
    dotnet tool run vpk -- pack --packId DAMiner --packVersion $Version --packDir $releasePublish --mainExe DAMiner.exe --packTitle DAMiner --packAuthors 'DAMiner contributors' --channel win --runtime win-x64 --icon src/DAGoldManager.Desktop/Resources/AppIcon/appicon.ico --delta None --outputDir $releaseOutput
    if ($LASTEXITCODE -ne 0) { throw 'Update packaging failed.' }
    Write-Output "Release assets ready in $releaseOutput. Upload the installer, portable ZIP, full package, and release feed together."
} finally { Pop-Location }
