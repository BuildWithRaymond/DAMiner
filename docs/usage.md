# Operating guide

## First run

1. In **Characters**, save character names and passwords. Uncheck **Can craft bars** for unregistered characters. They still launch and contribute gold or existing bars, but only registered crafters buy bars. An unregistered character can also be the courier. Password files are encrypted for your Windows user; SQLite contains no passwords.
2. In **Settings**, select the same supported DATester 7.41 `Darkages.exe` used by DAQuester. The included server profile and routes are for that client. Set the courier, banker mode, and correct weight per bar. For Local mode, save an enabled banker in Characters; DAMiner handles travel to Abel bank and selects Lamont automatically. For External mode, enter the already-running banker's name, map ID, and approximate coordinates.
3. In **Record setup**, choose a saved character and click **Launch and record**. The manager launches it and signs it in. A configured local banker with a saved password is launched too, so you can accept the sample trade in its window.
4. In that game window, go to Abel bank, open the bar-making NPC, and manually create one bar. Then complete a sample bar trade with your banker, accepting it in that window or through its existing automation; the recorder verifies server acceptance and the exact inventory change.
5. Return to the manager. Review the learned NPC, map, menu actions, gold cost, bar name, and verified sample batch. Click **Save learned setup**.
6. Press **Go**. Enabled characters run concurrently: each launches, signs in, reads gold, and starts funded crafting without waiting for every other login. Only native login input is serialized to protect desktop focus. Producers park on a free reachable tile on **row 8** inside Abel Storage, preferring **6,8**, and interact with Lamont across the counter. Leftover gold is pooled for further concurrent craft rounds; all producers then give bars to the chosen courier.
7. Only the courier trades to your banker. **Local** mode walks the banker to Abel bank, parks on an available row-8 tile, and waits for Lamont. The courier approaches its observed floor position; DAMiner verifies both sides of the trade and deposits the Gold Bars through Lamont. It confirms every deposit before another batch. **External** mode sends bars to the configured map/coordinates and waits the chosen interval after each accepted batch; the banker's own automation handles acceptance and deposits.

New installations choose Local mode and require your own banker name. Existing profiles keep their saved external behavior. For a banker already automated on another computer or VPS, choose **External** and enter its character name, map, coordinates, batch size, and deposit wait. No external banker password or VPS connection is needed on the sending computer.

Local bankers never craft or donate gold. DAMiner deposits existing carried Gold Bars as well as incoming batches; other items are left alone. Both producers and the local banker use the existing Abel walker and safe floor destination near **6,8**, away from the exits and Lamont's counter. Deposits require Lamont to be visible on **map 167** and use the same slot-based merchant protocol as DAOrganizer, with server-confirmed inventory changes and about 1.1 seconds between requests. Unsupported or ambiguous responses stop the run and preserve the ledger for review. Validate this on your server before unattended use.

The **Use** checkboxes on the **Operations** home screen enable or disable saved characters for the next run. Changes save automatically and appear on the Characters page too. Disabled characters are skipped; their accounts stay in the roster. These controls lock while a run or recording is active. Keep the chosen courier enabled, or select another enabled courier in Characters.

Recording uses direct game protocol observation; no MCP installation is required. Login and chat packets are excluded. A verified sample establishes a usable batch, not the server's absolute maximum. Record a larger successful trade to increase it. Dialog replay requires the recorded NPC and exact dialog text; changed menus stop the run and require new recording.

Verified craft progress saves automatically as a draft. **Stop recording** preserves it; **Resume recording** keeps the craft and lets you finish the trade later. Once both steps are verified, **Save learned setup** works even after Stop or reopening the app. The status beside the controls explains any missing step. Incomplete drafts do not enable Go.

**Pause** finishes the current atomic operation before pausing. **Stop** cancels commands and waits, leaving game clients open. An uncertain operation is preserved in History; inspect affected characters before acknowledging it and requesting a fresh run. Existing clients from other apps cannot be attached.

Unregistered donors hand off enough gold to fund each purchase without exceeding the receiver's carrying cap. This handoff remains active when **Pool leftover gold** is off; that setting controls pooling between registered crafters. Insufficient gold remains carried. If no registered crafter is enabled and enough gold exists for a bar, the run stops before moving it.

The server's explicit **Please register your character to use this feature** refusal switches that character to donor behavior. A refused craft is recorded as **Rejected** only when a fresh read confirms unchanged gold and bar counts. Timeouts and changed balances still require review. Missing login expiration text alone does not establish registration status; use the saved checkbox for known unregistered characters.

After each verified craft, the manager finishes informational **Next** pages and closes the final bank confirmation. It never chooses another purchase during cleanup. The ledger retains the verified craft if popup cleanup fails, and the run stops before further spending.

Local trades are verified from exact fresh changes on both managed characters, even when the server omits acceptance echoes. External banker trades still require server acceptance/completion and the courier's exact change. **Exchange completed.** is recognized as a completion response; its popup is dismissed after the operation is Verified. Popup text alone never proves a transfer. Unrelated or stale popups are left alone.

Before accepting a local gold trade, the receiver's server-confirmed exact one-way offer can establish the amount when the sender's echo is missing. Extra items, reverse gold, conflicting amounts, and cancelled offers still stop the trade. Errors identify the failed setup or verification stage.

Player approach stops on a free tile within two tiles of the visible player's current position. The walker reselects its destination if that player moves or another character occupies the planned tile. It does not continue walking closer once trade range is reached.

Per-character locks let independent movement and NPC dialogs overlap. A local trade locks both participants in a consistent order. One failed producer cancels and awaits the others before the run returns. Collection and banker delivery stay coordinated through the single courier. DAQuester's observed world-map nodes and acknowledged walker handle travel; the bank's floor destination is separate from Lamont's NPC position.

