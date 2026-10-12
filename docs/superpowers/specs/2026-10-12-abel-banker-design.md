# Local banker at Abel bank

Local mode launches the saved banker alongside producers and uses the existing
DAQuester navigation to Abel bank (map 167). It parks on an available row-8 floor
tile, preferring (6,8), away from the exits and Lamont's inaccessible counter.
Preparation completes only after Lamont is visible in Abel bank. The courier
approaches the banker's observed position, accepts each batch, and deposits each
bar through Lamont using the existing verified deposit protocol. Existing carried
bars are also deposited. No destination or NPC configuration is needed locally.

External mode keeps the saved player name, destination, and deposit interval,
preserving existing external profiles. Old deposit-NPC fields remain readable
for profile compatibility but no longer select a different local bank.

Navigation, NPC readiness, or deposit failure stops the run before another batch.
Ambiguous deposits remain in the operation ledger for review. Validate navigation
and NPC selection against simulated snapshots, incomplete NPC discovery, wrong
maps and duplicate entities. Cover local setup without an NPC setting, the
simulated full workflow, external compatibility, and the updated settings UI.
