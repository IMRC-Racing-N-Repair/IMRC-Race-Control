# Import a LiveTime roster

In IMRC Race Control, open **Saved Race Data → Import LiveTime Roster** and choose
a backup created in LiveTime's **Data** section. The preview counts active
drivers, classes, assignments, and any history in the source. Click **Import
Roster** to add the previewed records. Race Control backs up its current database
before applying the import.

The import adds names, nicknames, phonetic names, classes, minimum lap times
where present, and personal transponders assigned by class. Existing class
settings and driver assignments are kept. A racer with the same name is matched
only if there is a unique transponder match; conflicting and rental transponders
are listed separately. Review the resulting class settings before racing.

The reader supports recognizable LiveTime SQLite databases and the legacy
1 KiB page format verified against schema 193. If a legacy backup cannot be
read on its own, choose an older readable backup from the same installation
as the optional reference. Reference matching is checked before any roster
records are shown. Neither source file is changed.

Race results, individual laps, practice history, and rental inventory are
counted in the preview but are outside this roster import.
