# Code tour

`DAGoldManager.Updates` owns update preferences, serialized checks/downloads, source validation, package checks, and the installation mutex. `VelopackUpdateClient` exposes download operations only. Windows `DesktopProgram.Main` handles updater hooks before WinUI, blocks duplicate launches, and applies a pending package before any game model is constructed. The dashboard can stage updates but cannot restart a live run. Preview injects a disabled controller. Release builds embed their public GitHub repository; private game data stays outside the installation.

Start with `src/DAGoldManager.Core/RunCoordinator.cs`. Each producer's initial worker launches, scans, and crafts independently. `RunTogether` cancels siblings on failure and awaits every worker. `CraftAll` pools leftover gold without taking money from funded producers, then runs the next craft group concurrently. `DeliverAll` collects into one courier and keeps the external banker's deposit deadline across return trips. `VerifyOperation` writes the ledger before sending anything and records uncertainty rather than retrying.

The production roster excludes the banker in both modes. Local mode launches it alongside producers, walks it to Abel bank's safe row-8 floor, waits for Lamont, accepts the courier's exact bar offer, and journals one-bar deposits before allowing another batch. The courier approaches the banker's observed floor position. External mode never launches or controls the banker; it uses the saved destination and deposit interval. Older profiles deserialize as External, preserving their existing settings, while fresh profiles select Local with an empty banker name.

`BankerPreparation` shares `BankNavigation.AbelArrival` with producer navigation and checks that exactly one non-player Lamont is visible in Abel bank. `GameGateway.Banking.cs` rechecks this map and NPC before sending DAOrganizer-style slot deposits. `AbelBank` names the destination and floor coordinates; the old deposit-NPC setting remains readable for compatibility but does not override Lamont. `BankDepositPackets` owns the protocol format; `BankDepositEvidence` requires a fresh one-bar inventory decrease with stable identity, generation, map, gold, and unrelated items. Inventory confirmation establishes the observed deposit outcome; the program does not claim to have read a full bank balance. Popup cleanup and pacing follow the verified ledger entry. Ambiguity stops without retries.

`GoldPlanner` only does arithmetic. It does not know about windows, packets, or passwords. `CraftCooldowns` remembers recent successful crafts, including training, so the first Go respects that character's wait.

`SavedCharacter.CanCraftBars` distinguishes registered crafters from gold donors. The planner allows capped donors but selects only eligible receivers and funds one purchase at a time. Optional pooling can exclude registered donors without disabling mandatory unregistered handoffs. Registration refusals use fresh server evidence; `RunCoordinator` checks unchanged balances before recording a rejected craft and changing that character's role for the run.

`CraftDialogCleanup` finishes only informational Next pages from the recorded bank NPC. Menus and input prompts are dismissed without selecting or submitting anything. Cleanup runs after the verified craft ledger entry and before another craft; a popup failure preserves the successful purchase.

`IGameGateway` is the boundary between the workflow and the game. The real implementation is split into launch/navigation, crafting, and trading files. `SimulatedGame` implements the same interface with fictional characters. Tests and browser preview exercise the real coordinator through that interface.

`BankNavigation` distinguishes a floor destination from the recorded NPC's position. Abel uses a free reachable row-8 tile near (6,8), then opens Lamont's dialog without trying to stand beside his counter. The walker reselects a tile when an arriving player occupies it. `CharacterCommandLocks` locks only the affected character, or both local participants for a trade. The login input semaphore is separate and releases before waiting for in-game readiness.

Recording has two parts: `ManualRecording` translates allowed native packets into meaningful actions; `SetupRecorder` learns the recipe after actual gold and inventory changes. It retains menu choices and numeric crafting inputs, not raw login traffic. Recorded player greetings become `{character}`; prices remain exact. Trade display names are learned from known bar slots, then checked against the completed sample trade.

`TradeMonitor` reads the server's current offer and acceptance signals. A sent command is never proof of success. Trading also checks the partner ID, exact contents, sender inventory/gold, and the managed receiver's balances.

`TradeConfirmation` checks live snapshots while waiting for completion. Local trades require both participants' exact changes; external trades also require an acceptance/completion response. This avoids blocking on missing acceptance echoes before inspecting a completed local transfer. `TradePopupCleanup` recognizes the exact Exchange completed message. The ledger is written as Verified before popup dismissal, and the transport closes only the same observed popup without changing server evidence.

`GoldOfferEvidence` allows the managed receiver's exact one-way gold offer to confirm setup when the donor's optional echo is absent. It does not replace partner checks or balance verification. `GameGateway.Trading` includes the current stage in an error so window opening, offer setup, acceptance, and final verification can be distinguished.

Player navigation uses `StopWithinTolerance` and the visible entity ID. It keeps a safe current tile in trade range, tracks the player's latest position, and reselects occupied destinations. Ordinary coordinate walks retain their previous behavior; bank parking retains its row constraint.

`LocalStore` keeps settings and the operation ledger in SQLite. `PasswordVault` owns separate DPAPI-protected files. Passwords are never members of configuration or ledger records.

`DashboardModel` connects controls to the coordinator. Razor components display that model. The Windows MAUI host and simulation-only web host share those components; the preview host cannot open a real game connection.

`CharacterTable` exposes saved Use checkboxes on both Operations and Characters. They call the same `DashboardModel.SetEnabled` method, save automatically, and lock during running/recording. Full roster editing remains on Characters. The coordinator filters disabled accounts before launching or transferring anything.

`RecordingDraft` keeps verified craft and trade progress separate from the executable recipe. `DashboardModel.CaptureRecordingDraft` persists it when observation changes. Stop detaches observation without discarding the draft; starting again seeds `SetupRecorder` from it. `SaveRecording` requires both verified steps and promotes the draft to the recipe, whether observation is active or stopped.

Vendored DAQuester source remains in its original namespaces to make comparison with upstream straightforward. New application code lives under `DAGoldManager`. The original source applications and Excalibur are untouched.
