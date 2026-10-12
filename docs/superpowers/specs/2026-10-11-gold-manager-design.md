# DA Gold Manager

Initial workflow: save character names and passwords, press Go, automatically launch and sign in, craft at Abel bank, consolidate bars into one selected courier, and have that courier trade to the configured external banker. Local banker support and automatic Abel deposits are described in subsequent specifications.

## First-run recording

Users do not need to know NPC names or packet IDs. A setup recorder observes a manager-launched character while the user manually creates one bar and completes a sample trade. It records NPC identity/location, ordered semantic dialog actions, observed gold cost, bar item name, and the largest completed sample batch. It does not record login packets or passwords. A sample proves a usable batch size, not the server's absolute trade limit. Users may record a larger batch later. Gold-transfer actions are supported by the protocol but require verified balance changes on both managed participants.

The recipe becomes executable only after a complete observed craft with matching gold and inventory changes. The default expected cost is 50,500,000; an observed different cost is displayed for review and may be used only after saving setup. A second observed craft provides a cooldown interval hint; server outcomes always determine success. Setup stores configuration and recipes locally.

Verified craft progress is automatically saved as a draft. Stop ends observation but preserves the draft. Resume restores its recipe and proven trade batch, so a user can finish the trade later without paying for another demonstration craft. A complete draft remains savable after Stop or an application restart. Go still requires explicitly saving a recipe with a verified sample trade; an incomplete or errored draft cannot enable it. The setup screen always states whether observation is active and which evidence is missing.

## Execution

1. Validate saved roster, courier, recipe, supported game client, and recipient settings before launching.
2. Launch enabled producers and courier with saved passwords. Exclude the external banker, which is already running independently. Reuse this application's existing sessions. Do not attach to clients owned by another application or launch duplicate sessions for known connected names.
3. Scan confirmed identity, gold, inventory, weight, and location. Compute the theoretical production bound.
4. Craft immediately funded characters. Navigate using DAQuester's map graph, collision data, and confirmed walker. Re-read after every action.
5. Consolidate leftover gold to unlock additional crafts without exceeding 99,999,999. Continue until less than one bar's cost remains or a verified capacity restriction prevents progress.
6. After crafting is complete, bring producers to the courier. Collect as many bars as its verified free slots and weight allow. Include existing bars in delivery; report newly created bars separately.
7. Move only the courier to the external banker. Use the configured coordinate as a rendezvous hint, locate the actual visible player by name and ID, and approach its current position. Never trade to an NPC or use a stale player ID.
8. Offer the largest recorded/configured permitted batch. Accept only after the server acknowledges the exact expected offer with no incoming items or gold. The external banker's independent automation accepts and deposits bars. Never launch, move, or accept on behalf of an external banker locally. Producer-to-courier transfers automatically accept both local sides after matching their trade views.
9. Completion requires server acceptance evidence and exact donor inventory changes; managed transfers also require recipient inventory/balance changes. A single acceptance notification is insufficient.
10. Start the configured deposit wait after successful transfer to the external banker. Before any later banker trade, honor that wait, including when the courier must return for overflow stock. No wait is necessary after the final batch.
11. Reconcile ending gold, total bars, created bars, delivered bars, and any held stock. Store results and an operation ledger.

## Reliability and controls

One coordinator owns the run. The subsequent bank-arrival and parallel-workers design runs independent producer work concurrently while serializing native login input and coordinated trades. Each transaction has planned, started, verified, or needs-review outcomes in SQLite. Write started before sending. Incomplete transactions block blind restart; after inspection, users explicitly acknowledge and begin a fresh scan. Pause takes effect between atomic operations. Stop cancels movement/waits and marks any interrupted transaction for review. Characters and game clients stay open. A disconnect, changed identity, unexpected offer, or ambiguous craft stops automation with a clear explanation.

## Structure and readability

- Core: plain records, pure gold planning, run coordinator, verification rules, and interfaces.
- Game: DAQuester launcher/walker integration, packet-confirmed crafting/trading, first-run recorder.
- Storage: SQLite roster/settings/ledger; Windows DPAPI-protected password files.
- UI: shared Razor components with a Windows MAUI Blazor Hybrid host and a local simulated preview host.
- Vendor: pinned source copied from DAQuester, with license notices and small documented observer hooks. This avoids changing either source application or requiring them at runtime.

Use small named methods instead of dense expressions. UI has roster, courier selection, Go/pause/stop, recording setup, recipient/batch/wait settings, character progress, and history. Dark slate and restrained gold follow the supplied brief. Status is expressed in words, not color alone.

## Validation

Synthetic tests cover four capped characters creating seven bars, preserved leftovers, single courier, capacity overflow, exact batches, deposit waiting, partial/ambiguous effects, cancellation, recipe learning, packet framing, and persistence. Build the Windows host and inspect the shared UI in simulated mode. Live server menus, trade limits, and cooldown restrictions remain subject to the user's first-run recording; synthetic tests do not prove live behavior.
