# Approved implementation plan

1. Add synthetic navigation regressions for incomplete legacy exits, invalid and
   conflicting metadata, blocked triggers, complete-route preference, and fresh
   replanning. Add fast-mode preference/cadence and trade-response regressions.
   Run them against current behavior and record the expected failures.
2. Normalize the current DAQuester catalog and independently export Excalibur
   legacy exit hints. Produce coverage for all dimensioned maps and referenced
   legacy-only IDs. Extend the existing planner with a conservative next-hop
   fallback; never search beyond an unknown landing. Verify actual map changes
   before continuing travel.
3. Wire saved per-character fast mode to connected sessions, using the current
   DAQuester owned-character exemption and existing acknowledgment pipeline.
   Add the checkbox and pace text to the established character table.
4. Use configured bounded trade waits, capture fresh relevant server refusals,
   recheck both managed participants, and detect conflicting open exchanges.
   Preserve exact offer/balance verification and all historical review entries.
5. Run targeted and full simulated tests, review the diff, and build/publish into
   an isolated artifact directory. Check the simulation UI and release contents.
   Leave running DAMiner, Excalibur, DAQuester, and game clients untouched.

The historical sender-window failure remains a transient failure of unknown
cause. Distinguish a confirmed code defect from improved diagnostics; do not
claim live-server validation or add automatic transaction retries.
