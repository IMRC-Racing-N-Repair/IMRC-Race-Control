#!/usr/bin/env python3
"""Make a portable Race Control import copy from a LiveTime schema 325 backup."""
import argparse
from contextlib import closing
import getpass
import os
from pathlib import Path
import sqlite3
import tempfile


# Explicit allowlist. Account details, credentials, raw decoder traffic, device
# settings, contact data and pictures never go into the import copy.
FIELDS = {
    "SystemVersion": "Database",
    "Driver": "LID GUID IsActive FirstName LastName NickName PhoneticName",
    "RaceClass": "LID GUID IsActive Name Color",
    "RaceClassDefaultSetting": "LID RaceClassLID MinimumLapTimeSeconds",
    "EventRaceClassSetting": ("LID EventLID RaceClassLID MinimumLapTimeSeconds QualifyingStartTypeID "
                             "QualifyingLengthTypeID QualifyingLengthSeconds QualifyingLengthLaps "
                             "QualifyingNumberOfDriversPerRace MainStartTypeID MainLengthTypeID "
                             "MainDefaultMainLengthSeconds MainDefaultMainLengthLaps MainDefaultNumberOfDrivers"),
    "DriverRaceClassTransponder": "LID DriverLID RaceClassLID IsPrimary TransponderNumber",
    "DriverRaceClassCarInformation": "LID DriverLID RaceClassLID",
    "Event": "LID GUID Name IsActive StartDateTime EndDateTime",
    "Round": "LID EventLID IsActive TypeOrderNumber OrderNumber",
    "Race": ("LID GUID RoundLID RaceClassLID IsActive IsComplete Name TypeID "
             "OrderNumber RaceClassOrderNumber MainLetter MainNumber MultiMainCount StartTypeID "
             "LengthTypeID LengthSeconds LengthLaps MinimumLapTimeSeconds StartMicrosecondsUTC EndMicrosecondsUTC"),
    "EventRegistration": "LID DriverLID",
    "RaceEntry": ("LID RaceLID EventRegistrationLID OrderNumber CarNumber FinalPositionInClass CompletedLaps "
                  "CompletedTime FastestLap AverageLap Top5Average Top10Average Top15Average "
                  "IsDidNotStart IsDidNotFinish IsDisqualified"),
    "RaceTransponder": "LID RaceEntryLID TransponderNumber",
    "RaceLap": "LID RaceEntryLID IsValid LapTypeID LapNumber LapTimeSeconds ElapsedTimeSeconds MicrosecondsUTC",
    "PracticeLap": "LID",  # count only
    "TransponderRental": "LID TransponderNumber",  # exclude known rentals from personal tag import
}
OPTIONAL = {"PracticeLap", "TransponderRental", "DriverRaceClassCarInformation", "RaceClassDefaultSetting", "EventRaceClassSetting"}


def export(source, target):
    source.execute("PRAGMA query_only=ON")
    source.execute("PRAGMA trusted_schema=OFF")
    if tuple(source.execute("PRAGMA integrity_check(1)").fetchone() or ()) != ("ok",):
        raise ValueError("The source failed the SQLite integrity check.")
    if source.execute('SELECT "Database" FROM SystemVersion LIMIT 1').fetchone() != (325,):
        raise ValueError("This extractor supports LiveTime schema 325.")
    tables = {r[0] for r in source.execute("SELECT name FROM sqlite_schema WHERE type='table'")}
    for name, fields in FIELDS.items():
        if name not in tables and name in OPTIONAL:
            continue
        columns = [f'"{column}"' for column in fields.split()]
        column_sql = ",".join(columns)
        data = source.execute(f'SELECT {column_sql} FROM "{name}"')
        target.execute(f'CREATE TABLE "{name}" ({column_sql})')
        target.executemany(f'INSERT INTO "{name}" VALUES ({",".join("?" for _ in columns)})', data)
    for table, col in [
        ("Race", "RoundLID"), ("Round", "EventLID"), ("RaceEntry", "RaceLID"),
        ("RaceLap", "RaceEntryLID"), ("RaceTransponder", "RaceEntryLID"),
        ("EventRegistration", "LID"), ("Event", "LID"), ("Round", "LID"), ("Race", "LID"),
    ]:
        target.execute(f'CREATE INDEX "ix_{table}_{col}" ON "{table}" ("{col}")')
    target.commit()


def export_file(source, output):
    if output.exists():
        raise ValueError("Choose a new output filename. The exporter never overwrites files.")
    fd, temp_name = tempfile.mkstemp(prefix=".livetime-import-", dir=output.parent)
    os.close(fd)
    temp = Path(temp_name)
    try:
        with closing(sqlite3.connect(temp)) as target:
            export(source, target)
        os.link(temp, output)
    finally:
        temp.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("backup", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    with args.backup.open("rb") as f:
        plain = f.read(16) == b"SQLite format 3\0"
    if plain:
        source = sqlite3.connect(args.backup.resolve().as_uri()+"?mode=ro", uri=True)
    else:
        try:
            import apsw
        except ImportError as error:
            raise SystemExit("Install apsw-sqlite3mc to open a password-protected backup.") from error
        source = apsw.Connection(str(args.backup.resolve()), flags=apsw.SQLITE_OPEN_READONLY)
        key = getpass.getpass("LiveTime backup password: ")
        if not key:
            source.close()
            raise SystemExit("No password entered.")
        try:
            if not source.execute("PRAGMA cipher=sqlcipher").fetchone():
                raise SystemExit("The installed APSW build has no SQLCipher support. Install apsw-sqlite3mc.")
            source.execute("PRAGMA legacy=4").fetchall()
            source.execute(f"PRAGMA hexkey='{key.encode('utf-8').hex()}'").fetchall()
        finally:
            del key
    try:
        with closing(source):
            export_file(source, args.output)
    except Exception as error:
        raise SystemExit("Could not export the LiveTime backup. Verify the password, format and output path.") from None
    print("Import copy created:", args.output)


if __name__ == "__main__":
    main()
