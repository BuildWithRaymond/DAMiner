# Configurable bankers

Local banker travel and NPC selection are superseded by [Local banker at Abel bank](2026-10-12-abel-banker-design.md).

Support a local managed banker and an external banker already automated on another computer or VPS. New installations select Local mode with no banker name. Older saved profiles without a mode deserialize as External; their saved name, location, batching, and wait remain intact.

The banker is excluded from production, pooling, and reconciliation totals. Local mode requires an enabled saved banker with a password and a configured visible deposit NPC. Launch it alongside producers. Follow its actual live map/position, accept only the courier's expected one-way bar trade, verify both participants' balances, and journal each one-bar deposit. Cap batches by receiver capacity. Deposit acknowledged inventory before allowing another batch; ambiguity stops the run without retries. Existing banker bars are deposited too, with separate journal entries. Other items are untouched.

Use DAOrganizer's observed merchant deposit protocol (0x53 single item, 0x54 stack quantity) against the selected visible NPC. Require fresh identity, generation, map, exact bar decrease, unchanged gold and unrelated inventory. This shares behavior, not application source or dependencies. Live server compatibility still needs user validation.

External mode requires name plus map and coordinates, never launches or controls the banker, and keeps the confirmed exchange plus courier inventory proof and configurable deposit wait. Banker location has no hardcoded map in UI. Setup saves may be incomplete; Go requires complete setup.

Expose mode, banker name, deposit NPC (local), destination map/coordinates (external), batch size, and external wait in Settings. Local bankers can be saved in Characters and display a Banker role. Use generic public wording. Preserve simulation isolation and existing saved data; do not publish a repository or control Excalibur.

Verify fresh setup, old VPS configuration, custom external destination, local receipt/deposit batches, disabled banker, capacity, failed deposits, packet shape, persisted settings, full synthetic suite, UI workflows for both modes, and isolated Windows startup.
