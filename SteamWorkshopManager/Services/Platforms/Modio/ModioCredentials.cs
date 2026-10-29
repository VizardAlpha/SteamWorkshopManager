using System;
using System.Diagnostics.CodeAnalysis;

namespace SteamWorkshopManager.Services.Platforms.Modio;

/// <summary>
/// A user's own mod.io access: the OAuth token and the API path shown on mod.io/me/access.
/// <see cref="BaseUrl"/> always ends with <c>/v1/</c>.
/// </summary>
public sealed record ModioCredentials(Uri BaseUrl, string Token)
{
    // Keeps the token out of logs and exception messages.
    public override string ToString() => $"ModioCredentials({BaseUrl.Host})";

    /// <summary>
    /// Normalizes a pasted API path. Only HTTPS mod.io hosts are accepted: the token is sent
    /// wherever this points.
    /// </summary>
    public static bool TryParseBaseUrl(string? input, [NotNullWhen(true)] out Uri? baseUrl)
    {
        baseUrl = null;
        if (string.IsNullOrWhiteSpace(input)
            || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || !IsModioHost(uri.Host)
            || !uri.AbsolutePath.TrimEnd('/').Equals("/v1", StringComparison.OrdinalIgnoreCase))
            return false;

        baseUrl = new Uri($"https://{uri.Host.ToLowerInvariant()}/v1/");
        return true;
    }

    // Production hosts are u-{id}/g-{id}.modapi.io, the test environment *.test.mod.io.
    private static bool IsModioHost(string host) =>
        host.EndsWith(".modapi.io", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".test.mod.io", StringComparison.OrdinalIgnoreCase);
}
