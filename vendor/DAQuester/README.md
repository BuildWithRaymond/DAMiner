# DAQuester integration sources

Core, game protocol, launcher, walker, map graph, and functional collision data were copied from the local DAQuester source on 2026-10-11. Original MIT and third-party notices are retained here. No original application's files were edited.

The source repository's base HEAD was `bdd0bb09afaa41cd554ce748862c8b88f956c4f7`. This is a copy of its local working-tree files, not a runtime reference to that checkout.

Recording hooks consist of `StateTracker` publishing otherwise-unhandled packets to observers, and `ProxyConnection` publishing an allowlist of native gameplay actions. These hooks support setup recording and trade confirmation. They exclude login and chat traffic. Native movement, turns, clicks, dialog replies, and exchanges pass through without automatically interrupting automation. Native movement still invalidates queued movement projections so navigation reconciles from server evidence. Disconnects and unrecognized server state retain their interruption behavior.

Navigation additionally supports an optional `GameCommand.TargetRow`. `NpcApproach.Resolve` keeps substitute destinations on that row and excludes blocked/occupied/trigger tiles; `GameSession` reselects when another player takes the chosen floor tile. Gold Manager uses this for Abel's public bank floor. Existing commands without a row retain ordinary navigation. World-map selection and the acknowledged movement protocol remain the upstream implementation.

`DAGoldManager.Game.ExchangePackets` implements the corrected exchange shape evidenced by DAOrganizer's synthetic capture tests: AddItem has a slot byte and no quantity byte. Stack quantity follows only AddStackableItem. Gold Manager's exchange reader additionally accepts the documented optional completion subtype without inventing inventory state.

On 2026-10-13, route metadata was refreshed from the current local DAQuester
catalog. `tools/Import-Navigation.py` independently normalizes legacy Excalibur
exit metadata, excluding conflicting triggers and missing dimensions. These
hints retain an explicitly unknown landing. `RecordedExitHints` adds a one-hop
fallback to the existing complete planner; `GameSession` checks the observed
destination and replans from its real landing. The coverage file distinguishes
map references, geometry, and bank topology. No Excalibur source, game artwork,
or runtime data is bundled by that import.

`GameSession.MovementCadence` also matches the current DAQuester connected-owned-
character exemption. DAMiner supplies the predicate from its own authenticated,
connected sessions, saves a per-character preference, and enables the existing
bounded fast movement pipeline. This changes movement cadence only.
The current upstream `ProxyConnection.Movement` and `GameSession.FastMovement`
also accept a sole server-corrected staircase position without another refresh,
while retaining the refresh barrier for several requests still in flight. This
uses the actual server position and never publishes projected coordinates as
character evidence.

`ProxyConnection.PresentPopupCloseAsync` reports whether it dismissed the
expected popup. DAMiner retains verified trade cleanup evidence when a newer
popup replaces it before the presentation lock is acquired. This changes client
presentation only and never supplies transaction evidence.

`ProxyConnection.SendAsync` accepts an optional dispatch guard checked under
the server-write lock. A late exchange response or changed NPC approach can
skip an empty opening without consuming a packet sequence. DAMiner uses this
for bounded managed trade-opening recovery and fresh travel-NPC interaction;
offers, acceptance and paid travel choices are not replayed by that recovery.

Regional crafting adds the current DAQuester NPC-transfer model and guarded
ship, carpet and caravan execution. DAMiner retains its nullable landing and
one-hop hint handling. NPC approach coordinates cannot become walking exits.
Its gateway resumes only expected owned connection handoffs and journals travel
choices before submission through an optional session hook. Quoted gold loss,
unchanged inventory and an actual destination are required for fare receipts.
The upstream DAQuester and Excalibur working copies remain read-only references.

Travel NPC discovery first reaches the planner's safe interior approach, then
moves closer to the recorded stop within a bounded search if the NPC is still
unseen. Each acknowledged step excludes occupied tiles, exit triggers and the
NPC anchor itself; fresh NPC coordinates determine interaction range. Changed
ownership, location or popups stop discovery before opening a travel menu.

Travel visibility uses DAQuester's `NpcVisibility` ten-tile screen range,
independently of the three-tile walking approach radius. Quard's observed
position is behind the Loures Harbor Shop counter; a walkable tile within
three tiles of him is neither available nor required to open his menu.
