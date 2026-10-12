# Third-party notices

The root MIT license covers original DAMiner source and generated artwork. It does not replace upstream licenses or grant rights to Dark Ages artwork, clients, or dialogue.

## Imported engine

`vendor/DAQuester` contains selected engine sources from [BuildWithRaymond/DAQuester](https://github.com/BuildWithRaymond/DAQuester). Its [MIT license](vendor/DAQuester/LICENSE) and [upstream notices](vendor/DAQuester/THIRD_PARTY_NOTICES.md) remain intact. Upstream notices describe Arbiter, DAOrganizer, DALib, and other adaptations; their relative paths refer to the original DAQuester repository layout.

The imported Arbiter notice is at `vendor/DAQuester/DAQuester.DarkAges/Vendor/ARBITER-LICENSE.txt`. Distributions retain DAQuester and Arbiter notices under `licenses/`.

## Updates and dependencies

Updates and Windows packaging use [Velopack](https://github.com/velopack/velopack) 1.2.161 under MIT. Its complete notice is [licenses/Velopack-LICENSE.txt](licenses/Velopack-LICENSE.txt) and ships with the application.

Microsoft .NET, MAUI, Windows App SDK, SQLite, and restored dependencies retain their own licenses and runtime notices. xUnit, the .NET test SDK, and Playwright are development dependencies. Versions are declared in project files and the browser-check package lock.

## Game metadata and optional artwork

Route/walkability JSON contains factual navigation metadata. Game archives and rendered maps are read from the installed client, not distributed in source.

`tools/Initialize-Branding.ps1` draws an original gold-bar icon. `tools/assets/build-gold-icon.py` can create private local icons from a user-supplied sprite; it does not download artwork. Generated PNG/ICO files are ignored by Git. Their presence does not license the game's artwork under MIT.
