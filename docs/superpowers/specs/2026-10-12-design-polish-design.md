# DA Gold Manager design polish

## Purpose and scope

Make the Windows app feel like a carefully finished treasury tool for Dark Ages players. The main screen should answer three questions: which characters are participating, where the run stands, and how much reached the banker. Preserve the existing verified automation and saved credentials.

The user requested the actual gold bar sprite and a professional visual redesign, then asked to remove routine recording options because setup is complete. Use that authorization to implement and verify the result without another design approval gate.

## Direction

Choose a slate-blue treasury console. A fantasy parchment treatment would compete with operational data; a generic black dashboard would lose the game's identity. Use the authentic pixel-art bar as the single expressive element, with quiet controls and precise typography around it.

- Canvas: `#141d2b`; chrome: `#101827`; panel: `#1b2738`.
- Rules and raised controls: `#34445b`; text: `#eef2f8`; gold: `#d9b569`.
- Muted text and success/error colors are semantic derivatives, checked for readable contrast.
- Display: Segoe UI Variable Display, falling back to Segoe UI; body: Segoe UI; data: Cascadia Mono, Consolas, monospace. No remote font dependency.
- Layout: compact left navigation; consistent page header; one prominent run control; four aligned totals; a three-stop bank/courier/recipient route; a legible roster and activity feed.
- Signature: the original gold ingot, crisp at integer pixel scales, repeated only in the app mark and saved recipe.

```text
brand       | Operations                              local
navigation  | Ready                                  Go Pause Stop
            | carried gold | possible | created | delivered
            | Abel bank -------- courier -------- banker
            | Character roster                    Manage
            | Use / Character / Role / Gold / Bars / Status
            | Live activity
```

## Setup visibility

Once a recipe is saved and no draft is pending, hide Record setup from main navigation. Show saved NPC, location, cost, item, and trade batch in Settings. An Advanced setup disclosure contains a review button; it navigates to the existing recorder without starting recording or discarding the recipe. Missing recipes, pending drafts, and active recording retain the setup navigation. Saving a learned recipe returns to Operations. Daily-run empty text should reflect whether setup is saved.

## Components and states

Use one shared icon component for navigation and route symbols, one saved-recipe summary, semantic status chips, and a single CSS token system. Keep table controls accessible and preserve their existing labels. Distinguish disabled roster rows without fading their text below legibility. Busy, paused, stopped, completed, review, empty roster, and recording states keep explicit text; color is supplementary.

Use consistent 4px spacing, 8px panel corners, understated hover states, and visible keyboard focus. Tables scroll inside their containers. At the 760px desktop minimum, forms remain usable; at 430px, navigation becomes horizontal and panels stack. Respect reduced motion.

## Asset provenance

Use the exact gold bar PNG identified by the user: inventory sprite 4529 from C:/Users/Third/MerchantMode/merchantmode/public/items/4529.png. Copy that file unchanged into the UI and derive native PNG/ICO sizes with nearest-neighbor scaling. The installed client's archive mapping is not used to identify this asset. The user explicitly requested this asset, overriding the repository's default prohibition on committing game assets for this single app icon. Do not copy archives or unrelated sprites. Do not imply official affiliation.

## Verification and delivery

1. Verify a browser check fails on the old always-visible recording navigation.
2. Implement saved-setup navigation and visual changes.
3. Run the isolated seven-bar donor workflow, disabled-character workflow, recorder isolation, settings, history, and responsive checks.
4. Inspect screenshots for hierarchy, clipping, density, and contrast, then correct visible defects.
5. Publish the Windows app to artifacts/design-polish and smoke-test only an owned --demo process. Do not start a live run or touch Excalibur.

Design references: [Microsoft Fluent typography](https://fluent2.microsoft.design/typography), [Fluent layout](https://fluent2.microsoft.design/layout), and [Linear's UI redesign](https://linear.app/now/how-we-redesigned-the-linear-ui). Apply their consistent spacing and hierarchy principles to this product's content rather than copying a screen.
