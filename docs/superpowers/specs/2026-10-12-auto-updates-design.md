# DAMiner automatic updates

Use Velopack 1.2.161 for Windows release packaging, checksums, staged downloads,
and installation. Keep the existing game-data directory and encrypted passwords
outside the application install directory. The release repository will be named
DAMiner; its owner is not yet known. Until configured, updates make no requests.
Release builds embed their public owner/repository, and Settings allows an override.

Check stable GitHub releases on startup and every six hours. Automatically download
newer versions when enabled. Offline, missing releases, and failed verification
remain update status messages and never interrupt game automation or its ledger.
Manual checking works when automatic checks are off. Serialize checks and prevent
changing the source during a download or while a downloaded update is pending.

Bootstrap Velopack before WinUI initialization, with its implicit auto-apply turned
off. Acquire an installation-specific single-instance mutex before explicitly
applying a pending update at startup. A second launch exits without touching the
running application. Updates never restart an active session; a downloaded update
applies when the user next opens DAMiner. Do not stop or control other programs.
Simulation skips update bootstrap, network checks, persistence, and installation.

Settings shows current version, automatic-download preference, public repository,
Check now, progress, errors, and readiness for the next launch. A compact notice
on Operations identifies a downloaded update without blocking Go.

Provide a pinned local vpk tool, release script, and GitHub release workflow. Package
only a fresh publish directory, before running the app, excluding local profiles,
captures, databases, and browser caches. Emit portable ZIP, installer, full package,
and release feed; upload all release assets together. Validate controller behavior
with synthetic clients, package checks/downloads with Velopack's test locator,
startup hooks with the new executable, and UI states in an isolated preview.
