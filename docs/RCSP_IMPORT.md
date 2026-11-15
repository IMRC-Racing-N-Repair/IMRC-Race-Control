# RC Scoring Pro import — 0.17.1

## Source identification
- IMRC Race Control: SQLite with AppState, EventSessions and Drivers; use native restore.
- LiveTime: recognized plain SQLite with Driver and RaceClass; selective import and embedded-key path remain unchanged.
- RC Scoring Pro: Advantage ADT tables from a backup ZIP (or extracted Data directory). The reader checks the `Advantage Table` signature, field descriptor layout, record sizes, record counts, and live/deleted record flags. It does **not** execute embedded programs or modify the source.
- Unrecognized data must be blocked rather than guessed. Encrypted LiveTime backups without a recognizable SQLite header may require explicit LiveTime unlock selection.

## Verified structure of the supplied RCSP Data.zip
The root Data directory has **25 ADT table schemas** and all 25 record lengths match the declared field descriptions and physical record counts. This does not mean every cross-table relationship is complete.

| Source table | Live records | Physical records | Conversion |
| --- | ---: | ---: | --- |
| Tracks | 3 | 3 | Track names and IDs |
| Driver | 269 | 269 | IDs, name/nickname, transponder fallback |
| Classes | 42 | 42 | Class ID/name, minimum lap |
| DriverClasses | 991 | 991 | Driver–class assignment/transponder |
| Entries | 3,261 | 4,687 | Qualifying/main entry results, rounds, heats, positions, laps, times |
| Practice | 198,910 | 198,910 | Verified per-lap race crossings only |
| PracticeRuns | 6,247 | 6,247 | **Not yet imported:** standalone practice history |
| LoanerTransponders | 20 standard live rows | 113 physical | Live rental transponder numbers; other flag values are not assumed live |
| BestLaps | 50 | 50 | **Not yet imported** as historical track records |
| racerrecords | 213 | 213 | **Not yet imported** as historical driver records |

Other ADT tables include Country, QualPoints, Sponsors, Messages, PrintDrivers/PrintClasses, Debug, and settings. The ZIP also contains debug `.a` traces, indexes `.adi`, memo `.adm`, nested ZIPs, and blank/backup copies. These are not all imported.

## Race reconstruction
- RCSP has no equivalent of the LiveTime Event/Race/RaceEntry SQL relationship. The importer derives event groups from **track + Julian date**, and individual results from **date + race number + round + class + heat + round type**.
- The 3,261 live Entries rows include 3,257 entries with usable positive race numbers and association keys. The other 4 must not be assigned to an invented race.
- Race crossings in Practice are joined using **date + race number + round + class + driver + qualifying/main lap type**.
- Do not fabricate missing lap records. Keep officially saved summary lap counts/positions/times even if fewer lap crossings are recovered.
- In the supplied file, local analysis linked roughly **120,011** individually numbered race lap crossings from **123,162** officially scored laps among eligible entries (~**97.4%**). There are approximately **3,151** missing/unverified per-lap records. Confirm these counts on the production converter and check for collisions before calling the result lossless.
- The RCSP source remains unchanged. The existing Race Control importer reviews duplicates and backs up the destination before saving.

## Required release gate
1. Windows compile and regression tests.
2. Read the *real uploaded* RCSP ZIP with the converter on an isolated database, validate tracks/classes/drivers and every reconstructed event.
3. Verify specific examples of qualifying, mains, multiple-main legs, duplicate car numbers, transponder assignments and combined races.
4. Confirm lap numbers, crossing timestamps, elapsed times, average/fastest laps and position against RCSP printouts.
5. Compare imported roster/results/laps/practice history/records against source counts; explicitly account for every unmatched item.
6. Test conflict handling: merge, keep both, discard, Yes/No for all, second import, and results-only import.
7. Confirm imported historical events remain **Completed** (not active race day) and do not populate Next Up after app restart.
8. Preserve the existing LiveTime encrypted backup key behavior and verify native Race Control backup restore detection.
9. Do not publish v0.17.1 as fully supporting all RCSP data until practice history, record tables, and memo-dependent source fields have been handled or intentionally scoped out.

**Status:** Development branch / draft pull request. Do not equate successful compilation with data-fidelity verification.
