# Import LiveTime data

Open **Saved Race Data → Import LiveTime Data** and choose a readable LiveTime
backup or a portable import copy. The preview shows what's in the source and
what the current selection will add.

- Select **Drivers**, **Classes**, **Driver–class assignments**, and **Personal
  transponders** independently. Assignments need a matching driver and class.
  Choosing only transponders fills blank tags on existing assignments. Existing
  nonblank tags, class settings, and rental inventory are kept.
- For schema 325, select individual events or use **Select all**. Each selected
  event includes its completed qualifying races and mains with their saved
  finishing positions and totals. Leave **Include individual scored laps**
  checked for lap-by-lap history. Invalid laps and other timing records are
  excluded. Event history can be imported without adding to the Drivers or
  Classes lists; the event retains its historical display names.
- Review the counts, then click **Import Selected**. Race Control backs up its
  database before saving. Already imported events are skipped, so include laps
  when you first import an event if you want to keep its lap history.

Practice history, rental inventory, series, and LiveTime account/settings data
are outside this import. Class timing settings other than the available minimum
lap time use Race Control defaults; check them before using a class for racing.

## Open encrypted LiveTime backups

The app reads plain SQLite backups directly. It can also read the legacy 1 KiB
page format, verified against schema 193; if necessary, provide an older backup
from the same installation as a reference. Older schemas support roster import.

For SQLCipher backups (verified against schema 325), create a portable import
copy using a valid backup password. The exporter keeps only the columns used
for import, opens the source read-only, and does not save the password:

```sh
python -m pip install apsw-sqlite3mc
python scripts/Export-LiveTimeImport.py LiveTime-backup.db LiveTime-import.sqlite
```

Enter the backup password when prompted, then choose the resulting
`LiveTime-import.sqlite` in Race Control. Keep this portable copy private: it
includes names, transponder numbers, and race records. No account credentials,
contact information, photos, raw decoder traffic, or device configuration are
copied. Neither the backup nor a legacy reference is modified.
