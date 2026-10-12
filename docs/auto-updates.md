# Automatic updates

Use the **DAMiner portable ZIP** or **DAMiner installer** from a GitHub release.
Plain `dotnet publish` output is useful for development but does not contain the
installation metadata required for updates. Install or extract the new distribution
once; later versions can download automatically.

Settings shows the installed version, public GitHub repository, automatic-download
switch, Check for updates, progress, and errors. DAMiner checks stable releases at
startup and every six hours. Switching automatic downloads off still allows a
manual check. Downloads require the DAMiner package ID, a bounded package size,
and a SHA-256 checksum; Velopack verifies the download before marking it ready.
Offline or invalid releases leave the current app usable.

A ready update installs before the next normal launch, with no active run or
recording. Installation is not exposed to the running dashboard. An installation
mutex prevents a second launch from applying an update over a running instance.
The default account data remains in `%LOCALAPPDATA%\DAGoldManager\Data`; updater
preferences live separately in `%LOCALAPPDATA%\DAMiner\Updates`. An override that
puts game data inside the application directory blocks updates to preserve it.
`--demo` skips bootstrap, update requests, and update preference writes.

Official builds use `BuildWithRaymond/DAMiner`. Forks can set their own public
source in Settings, build with `-p:UpdateRepository=Owner/Repository`, or package
with `-Repository Owner/Repository`. GitHub release builds embed their own
repository automatically. No API token is stored or needed by users.

## Publishing a version

Create a stable GitHub release with a tag such as `v0.2.0`. The included release
workflow tests, builds, packages, and uploads assets when that release has no
attached assets. A release with manually supplied assets is left alone. Version
tags must contain three numeric components; prereleases are skipped.

For a local build:

```powershell
./tools/Publish-Release.ps1 -Version 0.2.0 -Repository BuildWithRaymond/DAMiner
```

For manual publishing, attach every generated asset to a draft release before
publishing it. Upload the files from `artifacts/releases`, including
`releases.win.json` and the full `.nupkg`, as well as the portable ZIP and installer.
Packages are built from a fresh publish directory. Do not replace them with a copy
of a folder used to run the app. Set a new version for each release rather than
replacing an already distributed package. The workflow never overwrites existing
assets. If an automatic upload fails partway through, complete it with the exact
original build's missing files, or publish a new version; do not rebuild and replace
files under the same version.

The SDK and local packaging tool are pinned to Velopack 1.2.161. See the official
[integration guide](https://docs.velopack.io/integrating/overview) and
[release workflow guide](https://docs.velopack.io/distributing/github-actions).
