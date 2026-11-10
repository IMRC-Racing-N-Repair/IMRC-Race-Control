# Import LiveTime data

Open **Saved Race Data → Import LiveTime Data** and choose a readable LiveTime
backup or a portable import copy. Each category button shows how many records
are in the backup. Click a button to include or exclude that category.

- Select **Drivers**, **Classes**, **Driver–class assignments**, and **Personal
  transponders** independently. Assignments need a matching driver and class.
  Choosing only transponders fills blank tags on existing assignments. Existing
  nonblank tags, class settings, and rental inventory are kept.
- The personal transponder count shows distinct numbers, excluding known
  LiveTime rentals. Existing records and conflicts can reduce what is added.
- Click **Import Selected** when ready. Race Control backs up its database
  before saving.

Enable **Events & races** to import historical events, completed qualifying heats
and mains, finishing results, and scored laps from schema 325. Expand **Choose
events** to select individual events. Events already imported are skipped.

Enable **Class settings** to apply supported settings to new or matching
classes. Minimum lap time comes from the class default when present. Qualifying
and main start style, timed/lap format, length, and capacities come from the most
recent active event with settings for that class. Existing custom main rules
are retained. Review the imported settings before using them for a new race.

Practice history, rental inventory, series, account data, and LiveTime-specific
options such as audio, flags, bump-up and multi-main scoring are outside this import.

## Open original LiveTime backups

Choose the original `.db` in Race Control. Race Control supports both
System.Data.SQLite RC4 and SQLCipher 4 backups. Enter the backup password when
prompted. For a SQLCipher backup you can also select **Choose Unlock Key File**
to open a matching `.livetime-key` file. Race Control previews the data in memory and does not save
the password, key, or a decrypted copy. Choose a standalone backup without a
pending journal. Encrypted backups up to 256 MiB are supported.

Plain SQLite and portable import copies also work. For the legacy 1 KiB page
format, verified against schema 193, you can provide an older backup from the
same installation as a reference. Older schemas support roster import.

For encrypted backups, a valid backup password or matching key is required.
Keep any unlock key private alongside its backup. Neither the original backup
nor the legacy reference is modified.


## Private installer with automatic unlock

An installer can include an encrypted passcode resource. When it is present,
Race Control tries it automatically before showing the manual unlock prompt.
The original backup stays read-only. Ordinary builds omit the resource.

On Windows, create the resource from a hidden passcode prompt, then pass its
path to the installer build:

```powershell
dotnet run --project scripts/LiveTimePasswordBundle -- "$env:TEMP\LiveTimePassword.bundle"
./scripts/Build-Installer.ps1 -Repository 'https://github.com/OWNER/REPOSITORY' -LiveTimePasswordResource "$env:TEMP\LiveTimePassword.bundle"
```

The resource and any installer that includes it must be handled like the
passcode: the app contains what is needed to unwrap it. Keep them out of
source control and public release artifacts.
