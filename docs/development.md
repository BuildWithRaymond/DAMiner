# Developer guide

## Build and tests

Develop the Windows host with .NET 10, `maui-windows`, and WebView2. `global.json` selects the SDK family. Packages and the Velopack tool have explicit versions.

```powershell
dotnet workload install maui-windows
./tools/Build.ps1
```

Focused checks:

```powershell
dotnet test tests/DAGoldManager.Tests -c Release -m:1
./tools/tests/Test-ReleaseContents.Tests.ps1
./tools/tests/Initialize-Branding.Tests.ps1
dotnet build src/DAGoldManager.Desktop -c Release -m:1
```

Tests use synthetic data. Windows password tests exercise DPAPI. No test signs in real characters.

## Simulation preview

```powershell
dotnet run --project src/DAGoldManager.Preview -c Release --urls http://127.0.0.1:5186
```

Preview always uses `SimulatedGame` and cannot connect to the game. In another terminal, with Chrome installed:

```powershell
npm ci --prefix tools/ui-checks
node tools/ui-checks/check.mjs
```

Artifacts are written under ignored `artifacts/`. Native `--demo` uses fictional characters and disables update requests/preference writes.

## Read the workflow

Read `Models.cs` for contracts, `GoldPlanner.cs` for pure decisions, and `RunCoordinator.cs` for workflow order. It launches producers concurrently, funds remaining purchases, collects into one courier, and delivers batches. `IGameGateway` isolates these decisions from native input and packets.

The real gateway has separate launch/navigation, crafting, trading, and banking files. Evidence helpers operate on immutable snapshots. `LocalStore` writes durable history before commands are sent. `DashboardModel` owns UI run/recording state. [The code tour](code-tour.md) explains remaining classes.

## Saved data

Profiles use `%LOCALAPPDATA%\DAGoldManager\Data`; the old name preserves compatibility. `DAGOLDMANAGER_DATA_DIR` overrides that directory. Passwords are separate DPAPI-protected files, bound to the Windows user.

Update preferences use `%LOCALAPPDATA%\DAMiner\Updates`. Keep game data outside the installation. Old profiles lacking banker mode remain External; fresh profiles choose Local with no banker name. Avoid renaming serialized fields without a migration.

## Icons and releases

Windows builds generate missing original icons through `tools/Initialize-Branding.ps1`. Existing artwork is preserved. For private sprite 4529 branding, use `python tools/assets/build-gold-icon.py <path-to-your-sprite.png>` with Pillow installed. Do not commit generated artwork.

Package with `./tools/Publish-Release.ps1`. See [automatic updates](auto-updates.md) for release tags/assets. Forks should pass `-Repository Owner/Repository` when packaging and `-p:UpdateRepository=Owner/Repository` when building.
