## Transponder rentals — 0.16.24

- Local Windows-targeted application build: zero errors or warnings.
- 913 regression checks pass, including canonical/duplicate IDs, duplicate aliases and color/number labels, unique random labels, more than 1,000 rentals, assignment/return, immutable completed lineups, skipped-week history, renamed/deleted entity snapshots and SQLite round trips.
- Windows packaging runs the real Transponder control with sample data in every theme. It checks visible duplicate errors, manual addition, selection and the three actual-use event history, and exports PNG previews.
- Existing installer checks cover fresh install, repair, upgrade from the retained public 0.16.23 and active-session protection.
- The live panel uses the existing decoder observation collection; no new polling or scoring path is introduced. Physical decoder hardware is not available in the build environment.

# Validation for 0.16.20

- Local Windows-targeted WPF build: passed with zero warnings and errors.
- Isolated race/scoring/database regression suite: 878 checks passed.
- Workflow YAML and current-version extraction from the single CHANGELOG.md: validated locally.
- Windows build and installer verification run on each relevant push. Consult the current Build and verify, Verify Windows installer, and Build draft release runs for results.
- Installer verification retains actual Windows-rendered approved RC logo previews for Dark, Light and Blue themes plus 16–256 pixel icon frames in its Brand artifact.

Interactive Windows checks: hover tooltips, the disabled empty Series screen, Settings > Application tools (including saved settings and active-session guards), theme preview/cancel, and the loading-screen logo. Also verify repair/reinstall and online updates using sample saved events before publishing.

If an earlier repair already deleted the old database, recovery requires a surviving database or backup; the installer cannot recreate deleted records.
