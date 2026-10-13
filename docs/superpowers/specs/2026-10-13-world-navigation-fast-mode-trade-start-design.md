# World navigation, DAQuester fast mode, and trade startup

Status: approved by the user on 2026-10-13; implementation in progress.

## Requested behavior

Account for the map information available in Excalibur and DAQuester so managed
characters can find their way to Abel bank from their current server-confirmed
location. Match DAQuester's current fast mode. Investigate transfers that stop
while opening the sender's exchange window.

Excalibur and DAQuester are read-only references. Do not launch, stop, restart,
attach automation to, or otherwise operate Excalibur or existing game clients.
Build and test with fictional characters and synthetic server messages.

## Findings

- DAMiner packages 1,432 map records and 7,680 directed portal edges. Its topology
  currently connects 606 recorded maps to Abel bank, map 167.
- The current local DAQuester catalog has 1,434 maps and 7,686 edges, adding the
  tutorial and Mileth inn room. It also contains several corrected records. It
  connects 608 recorded maps to Abel bank.
- Excalibur's WorldLogs do not add ordinary directed portals to DAMiner's current
  catalog. Its legacy `LoadMaps` code does contain 455 additional source map,
  destination map, and trigger-coordinate tuples. Only 222 of those currently
  have distinct known endpoints and source coordinates within known dimensions.
  Those valid tuples raise topological reachability to 1,078 known maps. This is
  a graph result, not proof that every exit is currently walkable on this server.
- Excalibur also references 92 legacy map IDs absent from the current dimensioned
  catalog. Account for those IDs in coverage as missing metadata, without giving
  them invented dimensions or collision grids.
- Both catalogs package collision metadata for 1,417 maps. Some map records lack
  usable geometry, and nine existing source portal coordinates exceed recorded
  map dimensions. Invalid metadata must be reported rather than trusted.
- `artifacts/navigation-design-coverage.json` accounts for the current catalog
  and candidate hints. Excluding invalid existing edges as well as invalid
  legacy hints leaves 1,077 catalog maps with candidate bank topology. Current
  packaged geometry matches 1,417 of the 1,434 modern records.
- DAMiner already vendors DAQuester's fast walking pipeline, but its gateway
  never enables `GameSession.FastMode`. The current local DAQuester additionally
  exempts its own connected characters from the nearby-player slowdown.
- All four supplied errors name the sender-window opening stage. The gateway
  waits a hardcoded eight seconds for a fresh exchange-start response, regardless
  of the configured trade timeout. The reported rows do not prove a transfer
  occurred. The outgoing exchange request shape agrees with Excalibur's request
  shape; Excalibur's transport trailer is added by DAMiner's encryptor already.
- The general state tracker filters out most system notices. The trade observer
  listens only for exchange packets, so a server explanation outside an exchange
  packet can be lost and replaced by a generic timeout.
- The user is unsure whether a window appeared, but reports that acknowledging
  the failed operation and trying again succeeds. This supports investigating
  transient readiness or response timing; it does not identify a confirmed cause
  and does not authorize automatic acknowledgements or retries.

## Approaches

Recommended: combine validated catalog data with explicit legacy exit hints,
replan after observed map transitions, and add precise trade-start diagnostics.
This substantially expands travel without inventing landing positions or
weakening transaction verification.

A smaller alternative is to copy the current DAQuester catalog and enable its
existing fast-mode switch. That adds two known maps but leaves the large legacy
connection gap and generic trade-opening errors.

## Navigation design

Normalize public route and walkability metadata into the existing content
directory. Refresh current DAQuester records and preserve verified modern portal
and internal-transfer data. Export legacy Excalibur exit hints separately with
source map, destination map, trigger tile, and provenance. Do not copy game
artwork, raw map assets, accounts, captures, or runtime databases into the repo.

Validate duplicate IDs, dimensions, source bounds, destination existence,
collision bit lengths, and profile consistency. A hint without a known landing
coordinate remains a hint. It must never become a fabricated complete portal.
Conflicting hints remain excluded unless existing metadata corroborates them.

Prefer a complete route through known portals and collision data. Where that is
unavailable, choose a reachable validated exit hint whose destination advances
toward the bank. Execute only the next transition, require a fresh server map and
position, and plan again from the observed landing. Do not project coordinates
through a sequence of unknown landings. Unexpected maps, unanswered transitions,
blocked exits, and missing collision data stop with the map and reason identified.

Preserve observed world-map node selection and the existing safe row-eight bank
arrival. Distinguish static route metadata from live player occupancy. Cache
invalidation must accompany any catalog changes; stale reachability or distance
fields must not select a removed connection.

Generate a coverage artifact accounting for every catalog map: bank topology,
geometry availability, invalid edges, and disconnected components. Covering every
known map does not guarantee an exit from inaccessible, server-specific, or
unrecorded areas. There is no automatic spending, recall-item consumption,
teleporting, or exploration of unknown exits in this change.

Include legacy-only referenced IDs with their missing-metadata reason. A map
must obtain real dimensions and usable collision information before it becomes
a walking target. Report coverage separately from actionable travel support.

## Fast-mode design

Add a saved per-character Fast mode preference, enabled by default as in the
current DAQuester. Bind it to `GameSession.FastMode` when the character connects
and update connected sessions when the checkbox changes. Place the control in
the existing character table, including a concise pace/status description.

Use the current DAQuester behavior: 160 ms in ordinary clear areas, 430 ms near
other players within its existing 20-tile Manhattan checks, 430 ms when location
is unknown, and normal pace on maps 10056 and 10180. Exempt only this manager's
own currently connected characters. A saved or disconnected roster name does
not exempt a nearby player. Turning fast mode off retains the existing configured
ordinary cadence. Preserve the bounded acknowledgment pipeline and correction
handling; fast mode must not speed up crafting or repeat financial transactions.

## Trade-start design

Use a bounded configured wait for exchange stages instead of silently imposing
an eight-second startup timeout. Keep listeners installed before the request and
keep both managed partners' identity checks. Identify separately whether the
sender or receiver has shown a fresh exchange-start response.

Observe relevant fresh non-chat server system messages during the attempt and
surface an explicit trade refusal when its text establishes one. Ignore stale
notices and unrelated chat. Retain a concise observation summary when no start
response arrives: stage, elapsed timeout, whether each managed window started,
current identity/map/range consistency, and any relevant server explanation. Do
not log passwords, complete packet payloads, or unrelated messages.

Recheck both local participants before sending the request. Detect a changed
generation, map mismatch, recipient movement out of range, or a conflicting open
exchange. Close only a recognized completed craft/trade dialog for which this
manager has already retained verification evidence. Do not cancel a user's
unrelated exchange, guess exchange preferences, or automatically toggle them.

The precise cause of the four historical failures remains unconfirmed until the
window observation or a server explanation distinguishes rejection from missing
response delivery. Add a regression fix for a demonstrated defect; do not claim
that diagnostic improvements alone establish a successful live transfer.

Require both partner windows and the exact one-way offer before acceptance.
Retain exact fresh gold/inventory verification after acceptance. Keep all old
NeedsReview entries intact and never automatically replay an ambiguous transfer.

## Verification and delivery

Use synthetic tests for catalog normalization, malformed and conflicting hints,
missing landings, blocked/unreachable exits, observed transition replanning,
world-map selection, safe bank arrival, and representative connected components.
Check the generated coverage report against the full catalog.

Test fast cadence with clear areas, restricted maps, nearby strangers, connected
owned characters, disconnected roster names, unknown position, and switch changes.
Verify preference round-tripping and compatibility with existing saved profiles.

Use synthetic exchange events to test delayed starts, explicit refusal, stale
notices, missing responses on either side, partner/generation changes, conflicts,
cancellation, and exact balance verification. Retain all existing transaction and
concurrency regression checks.

Run the full simulated suite and build the Windows host in an isolated output
folder, because the existing DAMiner session may be running live characters.
Report the build location and that opening the new build is required to use the
changes. Do not stop a running application or launch any live automation.
