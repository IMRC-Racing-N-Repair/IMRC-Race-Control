using System.Reflection;
using Velopack;
using Velopack.Sources;
namespace IMRCRaceControl.Services;
internal static class ApplicationUpdateService
{
    public static string Repository => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value ?? "";
    public static UpdateManager? CreateManager()
    {
        if (!Uri.TryCreate(Repository, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
            uri.AbsolutePath.Trim('/').Split('/').Length != 2) return null;
        return new UpdateManager(new GithubSource(Repository, accessToken: null, prerelease: false));
    }
}
