# Validation for 0.16.19

- Local Windows-targeted WPF build: passed with zero warnings and errors.
- Isolated race/scoring/database regression suite: 878 checks passed.
- Workflow YAML and current-version extraction from the single CHANGELOG.md: validated locally.
- Windows build and installer verification run on each relevant push. Consult the current Build and verify, Verify Windows installer, and Build draft release runs for results.
- Installer verification retains actual Windows-rendered logo previews for Dark, Light and Blue themes in its Brand artifact.

Interactive Windows checks: hover tooltips, the disabled empty Series screen, Settings > Application tools (including saved settings and active-session guards), theme preview/cancel, and the loading-screen logo. Also verify repair/reinstall and online updates using sample saved events before publishing.

If an earlier repair already deleted the old database, recovery requires a surviving database or backup; the installer cannot recreate deleted records.
