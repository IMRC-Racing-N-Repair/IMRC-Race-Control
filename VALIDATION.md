# Validation for 0.16.18

- Windows-targeted WPF application build: passed locally with zero warnings/errors; GitHub Windows build also passed.
- Isolated race/scoring/database regression suite: 878 checks passed locally. Windows default-mode checks: 845 passed.
- Added repair simulation, SQLite WAL migration, pre-restore safety snapshot, missing database recovery, invalid backup rejection, and storage separation checks.
- Logo composition inspected using the original IMRC lettermark and RACE CONTROL wording.
- Windows self-contained Setup.exe packaging and native PNG/ICO generation: passed in Verify Windows installer. Rendered Windows logo inspected.
- Draft release uploads are checked for Setup.exe, package, update feed and checksums; refer to the latest Build draft release run for upload status.

Before publishing: install Setup.exe on Windows, migrate sample earlier data, run repair/reinstall, restart, and verify the saved event, drivers, settings and practice history. Also verify updating from a published release, offline launch, declined updates and blocked updates during race/practice. Automated builds and simulated installation-folder deletion do not replace these interactive acceptance checks.

If an earlier repair already deleted the old database, recovery requires an existing database or backup; this release cannot recreate deleted records.
