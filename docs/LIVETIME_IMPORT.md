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

## Open original LiveTime backups

Choose the original `.db` in Race Control. For a SQLCipher 4 backup, enter the
backup password or select **Choose Unlock Key File** to open a database-specific
`.livetime-key` file. Race Control previews the data in memory and does not save
the password, key, or a decrypted copy. Choose a standalone backup without a
pending journal. Encrypted backups up to 256 MiB are supported.

Plain SQLite and portable import copies also work. For the legacy 1 KiB page
format, verified against schema 193, you can provide an older backup from the
same installation as a reference. Older schemas support roster import.

For encrypted backups, a valid backup password or matching key is required.
Keep any unlock key private alongside its backup. Neither the original backup
nor the legacy reference is modified.
