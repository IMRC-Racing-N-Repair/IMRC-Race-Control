using System.Text;

namespace IMRCRaceControl.Services;

/// <summary>Low-volume connection diagnostics; never throws into scoring or transport code.</summary>
public static class DecoderDiagnostics
{
    private static readonly object Gate = new();
    public static string? LastWriteError { get; private set; }
    public static void Write(string context, string message, Exception? exception = null)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppCrashLogger.LogDirectory);
                var path = Path.Combine(AppCrashLogger.LogDirectory, $"decoder-diagnostics-{DateTime.Now:yyyyMMdd}.log");
                if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024)
                    File.Move(path, path + ".previous", true);
                var version = typeof(DecoderDiagnostics).Assembly.GetName().Version;
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{context}] v{version} PID {Environment.ProcessId} {message}\n{exception}\n", Encoding.UTF8);
                LastWriteError = null;
            }
            catch (Exception ex) { LastWriteError = ex.Message; }
        }
    }
}
