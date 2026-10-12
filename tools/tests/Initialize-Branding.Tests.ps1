$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $PSScriptRoot "../../artifacts/branding-test-$([Guid]::NewGuid().ToString('N'))"
$initializer = Join-Path $PSScriptRoot '../Initialize-Branding.ps1'
if (-not (Test-Path -LiteralPath $initializer)) { throw 'Missing public branding initializer.' }

& $initializer -Directory $testRoot
$webIcon = Join-Path $testRoot 'src/DAGoldManager.UI/wwwroot/images/gold-bar.png'
$nativeIcon = Join-Path $testRoot 'src/DAGoldManager.Desktop/Resources/AppIcon/appicon.ico'
Add-Type -AssemblyName System.Drawing
$image = [Drawing.Image]::FromFile($webIcon)
try {
    if ($image.Width -ne 64 -or $image.Height -ne 64) { throw 'Web icon dimensions are incorrect.' }
} finally { $image.Dispose() }
$icon = [IO.File]::ReadAllBytes($nativeIcon)
if ([BitConverter]::ToUInt16($icon, 2) -ne 1 -or [BitConverter]::ToUInt16($icon, 4) -ne 7) {
    throw 'Expected a Windows icon containing seven sizes.'
}

# Local sprite artwork must survive subsequent builds, byte for byte.
[IO.File]::WriteAllText($webIcon, 'local sprite sentinel')
& $initializer -Directory $testRoot
if ([IO.File]::ReadAllText($webIcon) -ne 'local sprite sentinel') { throw 'Existing local artwork was overwritten.' }
Write-Output 'Branding checks passed: generated icons are valid and existing artwork is preserved.'
