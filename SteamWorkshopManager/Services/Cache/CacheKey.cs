using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamWorkshopManager.Services.Cache;

/// <summary>
/// Address of a cache entry: <c>platform/scope/name</c>, where name may hold sub-paths
/// (<c>item-42/text-french.json</c>). Deleting a session drops its whole scope.
/// </summary>
public readonly partial record struct CacheKey
{
    public const string Steam = "steam";
    public const string ModIo = "modio";
    public const string Shared = "shared";

    public string Platform { get; }
    public string Scope { get; }
    public string Name { get; }

    public CacheKey(string platform, string scope, string name)
    {
        Validate(platform);
        Validate(scope);
        foreach (var part in name.Split('/')) Validate(part);

        Platform = platform;
        Scope = scope;
        Name = name;
    }

    public static string AppScope(uint appId) => $"app-{appId}";

    public static CacheKey SteamApp(uint appId, string name) => new(Steam, AppScope(appId), name);

    /// <summary>Remote images keyed by URL: hosts change the URL when the image changes.</summary>
    public static CacheKey Image(string url) =>
        new(Shared, "images", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32].ToLowerInvariant());

    internal string RelativePath => Path.Combine([Platform, Scope, .. Name.Split('/')]);

    public override string ToString() => $"{Platform}/{Scope}/{Name}";

    // Keys are built in code: anything else is a bug, never user input to escape.
    private static void Validate(string segment)
    {
        if (!SegmentRegex().IsMatch(segment) || segment.All(c => c == '.'))
            throw new ArgumentException($"Invalid cache key segment '{segment}'.");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex SegmentRegex();
}
