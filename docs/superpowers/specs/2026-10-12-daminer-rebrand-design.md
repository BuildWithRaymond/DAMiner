# DAMiner rebrand

The user selected DAMiner as the new product name. Rename the sidebar wordmark, footer, browser titles, loading text, native window title, Windows application metadata, and executable. Retain the approved gold bar sprite 4529 and visual design. Use “Gold operations” under the wordmark to describe the app's purpose.

Publish a new self-contained Windows folder at artifacts/daminer with DAMiner.exe. Keep internal project paths and C# namespaces stable; explicitly preserve the desktop root namespace when changing its assembly name. These implementation identifiers are not the product brand.

Keep the existing local data directory, database name, password files, and legacy data-directory override intact so the rename preserves saved characters, recipes, and history without a migration. Document this compatibility in README. Do not launch a live run or stop Excalibur.

Validation: compile shared UI and preview, run existing simulated browser checks, inspect the updated wordmark, publish Windows, and smoke-test an isolated --demo process with the new title and executable. Check the complete folder includes the renamed window icon and static assets. No new tests are needed for copy edits; existing workflow checks cover the interface.
