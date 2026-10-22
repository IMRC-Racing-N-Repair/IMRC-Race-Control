namespace IMRCRaceControl.Services;

public static class ApplicationDataPaths
{
    // This is user-owned data, never the Velopack installation directory.
    public static string LocalRoot => RequireLocalRoot();
    public static string Root => Path.Combine(LocalRoot, "IMRC Racing N Repair", "Race Control");
    public static string DataDirectory => Path.Combine(Root, "Data");
    public static string BackupDirectory => Path.Combine(Root, "Backups");
    public static string LegacyDirectory => Path.Combine(LocalRoot, "IMRCRaceControl");
    public static string DatabasePath => Path.Combine(DataDirectory, "RaceControl.db");
    private static string RequireLocalRoot()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            throw new IOException("The Windows user data folder could not be found. Race Control will not create a database in the program folder.");
        return path;
    }
}
