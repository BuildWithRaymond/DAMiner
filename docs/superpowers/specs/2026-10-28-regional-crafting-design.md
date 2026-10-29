# Regional crafting and courier travel

Medenia producers craft at Kelan in Noam Bank (10061); Temuair producers craft at
Lamont in Abel Storage (167). The configured courier collects at each producer's
location and delivers to the configured banker. Existing external destinations
remain settings, not hardcoded character names.

Bank selection uses the validated local route graph, including Medenia caravans
but excluding the Quard ship and Habab carpet crossings. Low-numbered Medenia
maps therefore remain supported. The selected bank survives the courier's
continent crossings. Noam's interior target is (8,8), away from exit (14,6).

All launch locations are scanned before spending. Producers then craft
concurrently. Pooling stays within bank groups; crossing continents to combine
small leftovers is deliberately excluded. Mixed-region couriers, and couriers
starting in Noam, keep 100,000 gold outside crafting and pooling. Same-bank peers
can fund this reserve before any purchases. Bank-arrival fares require a fresh
balance check before deciding to craft.

The existing demonstrated recipe supplies item, price, cooldown and exact
semantic dialog guards. Regional adaptation changes NPC and destination only.
An unknown menu stops rather than guessing new purchase terms. Cleanup uses the
same regional NPC. All craft and exchange balance evidence remains unchanged.

DAQuester's explicit NPC graph edges and guarded dialogue execution supply ship,
carpet and caravan travel. Estimated route landings rank paths only. Actual
server map and position establish arrival. Imported legacy NPC approach tiles
cannot become walking portals. Existing unknown-landing hints still execute
one hop and replan from the real landing.

Travel submissions carry immutable character, generation, expected destination,
quoted fare and inventory evidence. An expected redirect can resume only on a
newer owned connection for the same authenticated character. Manual input and
ordinary disconnects still interrupt. Exact fare debits plus unchanged inventory
are counted once, separately from crafting costs. Submission is durable before
sending; only verified receipts resolve its ledger record. Ambiguous payments
are never resent automatically.

Verification uses fictional snapshots and loopback transports: regional pools,
courier collection, reserve funding, carrying caps, bank arrival, travel route
selection, changed menus, delayed gold evidence, exact fares, cancellation,
replacement connections, and durable ambiguity. No live character or Excalibur
process is operated.

Verification completed: 293 simulated C# tests and 10 navigation-import tests
passed. The Windows build completed with no warnings or errors at
`artifacts/desktop-regional-banks/DAMiner.exe`. Package inspection found no
runtime data, and bundled route hints match the source. Live travel and Kelan's
server dialogue were not operated; the exact saved purchase guards remain in
force.
