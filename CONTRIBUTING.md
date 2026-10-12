# Contributing to DAMiner

Start with [README.md](README.md) and [the code tour](docs/code-tour.md). Bug reports should describe expected behavior, actual behavior, version, and reproducible steps. Feature proposals should explain the user problem. Discuss larger workflow changes in an issue before a pull request.

## Local development and review

Use Windows, .NET 10, the `maui-windows` workload, and WebView2. Run `./tools/Build.ps1` to test and build. The [developer guide](docs/development.md) includes focused checks and the simulation-only preview.

Create a branch from `main`, make a focused change, and open a pull request. Explain what changes for the user and list validation performed. Windows checks must pass. Maintainers review transaction behavior and compatibility before merging. Contributions use the MIT license; retain existing third-party notices.

## Readable C#

- Prefer descriptive names, small methods, explicit states, and ordinary control flow.
- Summarize each new class's purpose and dependencies.
- Comment why a guard, evidence requirement, concurrency choice, or compatibility rule exists. Explain packet fields at the protocol boundary.
- Keep comments beside the relevant code and update them when behavior changes. Avoid comments that repeat obvious assignments.
- Keep Core independent of Windows, UI, packets, and storage. Extend `IGameGateway` and the simulator together.
- Preserve upstream namespaces and notices in `vendor/`. Explain vendor changes separately so they can be compared with upstream.

## Validation

Run simulated .NET tests and build Windows. Transaction changes need regression coverage for successful, stale, missing, and conflicting evidence. UI changes need browser checks and inspection at desktop and narrow widths. Documentation-only changes need wording/link review, not new tests.

Do not test contributions by launching real characters. Do not close or restart `Excalibur.exe`. Stop must leave game clients open; updates must never restart a running workflow.

## Data and recovery

Never commit character accounts, passwords, game artwork/client archives, packet captures, runtime databases, browser caches, or personal screenshots. Use fictional fixtures and review `git diff --cached` before committing.

Crafts/transfers require fresh server evidence. A submitted command or completion popup is insufficient alone. Preserve append-only history, record ambiguous outcomes as needing review, and require a fresh scan after user acknowledgement. Never add automatic replay of an ambiguous transaction.

Passwords stay separate from settings/history and use Windows DPAPI. Public profiles require the user's own banker. New fields must keep old profiles readable.
