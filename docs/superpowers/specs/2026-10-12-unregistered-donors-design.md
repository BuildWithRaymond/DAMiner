# Unregistered gold donors

The roster previously treated every enabled character as a crafter. Unregistered accounts cannot buy gold bars and must fund registered characters instead. The server refusal is `((Please register your character to use this feature.))`.

Each saved character gets a `CanCraftBars` setting, defaulting to true for existing rosters. The Characters table exposes this setting. Updating a password preserves it and the enabled setting. A donor can also be the courier and can contribute existing bars.

The planner chooses receivers only among eligible crafters. Unregistered characters may donate even when holding more than a bar's cost. Each transfer fills only the receiver's deficit, respects the carrying cap, and verifies both balances. Donor funding remains enabled when optional pooling of registered characters' leftovers is off. Repeated craft rounds consume all usable gold; final insufficient gold remains carried.

Explicit server registration refusals are tracked per managed connection and prevent further crafting. A refusal during a recipe becomes a rejected operation only when a fresh read confirms unchanged gold and bars. Changed balances, disconnects, cancellation, and timeouts retain NeedsReview. Earlier ambiguous crafts are not retroactively acknowledged or reclassified.

Missing login expiration text alone is not reliable negative evidence: messages may be delayed or missed. Saved eligibility provides a deterministic override; explicit refusal provides automatic detection. No guessed registration status permits a replay of an ambiguous operation.

Independent eligible producers keep concurrent launch and craft behavior. Transfers, collection, and banker delivery remain coordinated. If enough gold exists but no registered crafter is enabled, stop with a clear error before any transfer. Existing bars can still be delivered when no new bar can be funded.

The user also reported unfinished prompt popups after successful crafts. Recording stops collecting recipe actions at the verified gold/bar change, so later confirmation clicks were omitted. After recording a verified craft in the ledger, cleanup waits for its confirmation if needed, advances only informational Next pages from the bank NPC, and dismisses menus/input prompts without selecting a purchase. Repeated pages and excessive page chains stop safely. Cleanup failure leaves the successful craft Verified and prevents another craft.

Implementation checklist:

- Reproduce donor routing with synthetic failing tests.
- Add saved eligibility, planner donor rules, and coordinator filtering.
- Test fresh registration rejection and ambiguous balance changes before implementing their handling.
- Add protocol refusal observation and saved roster controls.
- Verify persistence, concurrent crafting, existing delivery, and review behavior.
- Run browser simulation checks; publish a separate Windows folder. Do not control live clients or Excalibur.
