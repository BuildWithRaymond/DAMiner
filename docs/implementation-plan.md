# Implementation checklist

- [x] Add pure gold planner and verified run coordinator; demonstrate failing behavior tests first.
- [x] Preserve DAQuester navigation/login sources and licenses; add narrow packet observer hooks.
- [x] Add craft recipe recorder and exact trade verification with synthetic packet tests.
- [x] Add encrypted account persistence, recipe settings, and durable SQLite ledger.
- [x] Build shared readable Razor dashboard, MAUI Windows host, and isolated simulated preview.
- [x] Run tests, build Windows release, inspect UI, document setup and limitations.

Validated with 33 passing synthetic tests and a clean Release solution build. Browser checks completed the seven-bar example, checked history and settings, confirmed recording is disabled in preview, and verified the compact layout. Initial, completed, settings, and compact screenshots are under ignored `artifacts/ui-checks`.

The recording recovery fix adds four regression tests (37 total passing): saving after Stop/reopening, restoring an incomplete craft draft without enabling Go, keeping errors visible, and retaining the recipe and proven batch on resume. The updated release is `artifacts/recording-fix`; its isolated desktop demo startup and updated browser checks passed. The recording status screenshot was inspected. The old app's already-erased, unsaved recipe cannot be recovered by this update.

The bank arrival/concurrency fix passes 49 tests. New tests reproduce the unreachable Lamont counter on the actual bundled grid, verify a reachable row-8 floor target and Abel world-map selection, enforce row-only occupied-tile alternatives, overlap producer launch/crafting, and confirm cancellation drains sibling work and releases participant locks. The release is `artifacts/parallel-bank-fix`. Its isolated desktop demo startup and full browser checks passed, including the seven-bar simulation and readable character statuses. Saved recipes remain compatible; live bank interaction still requires the user's run. The existing real manager, game clients, and Excalibur were not stopped or restarted.

Published the self-contained Windows release to `artifacts/desktop`. The published executable passed an isolated `--demo` startup check. Its folder includes route data and upstream license notices. Only task-owned simulation helpers were closed after validation.

An external banker is already logged in with exchange acceptance enabled. A regression test proves it is never launched locally or included in production, even if an old roster contains it. The courier sends bars and honors the deposit wait. Live game sessions were not used for validation; first-run recording is required to establish actual NPC menus and a usable trade batch.

The brainstorming skill's referenced writing-plans skill is not installed. This repository checklist records the implementation sequence directly. The user approved the workflow and then explicitly requested code, including first-run recording and readable source.
