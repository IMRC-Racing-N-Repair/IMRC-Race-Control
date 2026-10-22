using System.Text;

namespace IMRCRaceControl.Services;

public static class AppCrashLogger
{
    private static readonly object Gate = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IMRCRaceControl",
        "Logs");

    public static string Log(string context, Exception exception)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"RaceControl-{DateTime.Now:yyyyMMdd}.log");

            var sb = new StringBuilder();
            sb.AppendLine(new string('=', 88));
            sb.AppendLine($"{DateTimeOffset.Now:O}  {context}");
            sb.AppendLine($"Process: {Environment.ProcessId}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($".NET: {Environment.Version}");
            sb.AppendLine(exception.ToString());
            sb.AppendLine();

            File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }
    }
}
