# Confirm local trades from both balances

A local gold transfer completed in the game but timed out in the manager. The failure lasted eight seconds, matching setup/offer waits rather than the configured 120-second completion wait. The old error did not identify the stage, so it could not distinguish which setup response was missing. The gateway also previously waited for exchange completion or both acceptance signals before inspecting balances, which can reject completed local trades when acceptance echoes are absent.

For gold trades, the managed receiver's exact server-confirmed one-way offer can establish the amount even if the donor's own gold echo is absent. Require both correct partner windows, no items, no reverse gold, no cancellation, and no conflicting donor amount. This changes offer confirmation, not transaction confirmation. Identify the current trade stage in errors so another missing response can be traced without guessing.

After both local accept commands are sent, check the observed offer and fresh server balances together. A local trade is confirmed by the exact gold/bar change on both managed participants, including unchanged gold for bar trades and unchanged bar counts for gold trades. Require connected identities, matching session generations, and revisions newer than the pre-trade baselines. Offer/partner changes, cancellation, decoding errors, and mismatched or one-sided balances remain failures requiring review.

External banker trades retain the requirement for server completion or both acceptance signals, in addition to the courier's exact gold/inventory change. A generic popup or a sent Accept command alone is never transaction proof.

Recognized completion popups can be dismissed after verification. Popup presentation must not fabricate server balances or close unrelated messages. Earlier ambiguous operations are not reclassified or acknowledged automatically.

The user confirmed the exact popup text: `Exchange completed.`. Track exchange-message and generic server popups with their connection generation and server revision. After the ledger records Verified, dismiss only this fresh completion popup on managed participants. Client ClosePopup presentation does not alter the server snapshot.

Courier approach could stop near the banker with Destination remains occupied after three route refreshes. Destination reselection previously applied only to row-constrained bank parking, and tolerance resolution continued walking closer even after reaching trade range. Extend reselection to tolerant player approaches, stop on an already safe tile within two tiles, and use the visible entity's current position when it moves. Preserve ordinary coordinate-walk and bank-row behavior. Tests cover current trade range, occupied destination substitution, and excluded doorway triggers.

Implement with synthetic regression tests, run the existing full suite, publish in a separate folder, and leave Excalibur and live clients untouched.
