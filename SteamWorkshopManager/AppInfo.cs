using System.Reflection;

namespace SteamWorkshopManager;

public static class AppInfo
{
    public static string Version { get; } = GetCleanVersion();

    public static string VersionWithPrefix => $"v{Version}";

    private static string GetCleanVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "0.0.0";

        // Remove git hash suffix (e.g., "1.0.0+abc123" -> "1.0.0")
        var plusIndex = version.IndexOf('+');
        return plusIndex > 0 ? version[..plusIndex] : version;
    }
}
