using System.Text.Json;

namespace SteamWorkshopManager.Services.Steam;

/// <summary>
/// Extracts the <c>data</c> node from a Store <c>appdetails</c> response.
/// </summary>
public static class SteamAppDetailsParser
{
    /// <summary>
    /// Steam sometimes keys the entry by another id (e.g. Bellwright 1812450 comes back
    /// under its DLC 3100670), so fall back to matching <c>data.steam_appid</c>.
    /// </summary>
    public static bool TryGetData(JsonElement root, uint appId, out JsonElement data)
    {
        data = default;
        if (root.ValueKind != JsonValueKind.Object) return false;

        if (root.TryGetProperty(appId.ToString(), out var exact) && TryGetSuccessfulData(exact, out data))
            return true;

        foreach (var entry in root.EnumerateObject())
        {
            if (!TryGetSuccessfulData(entry.Value, out var candidate)) continue;
            if (candidate.TryGetProperty("steam_appid", out var idProp) &&
                idProp.ValueKind == JsonValueKind.Number &&
                idProp.TryGetUInt32(out var id) && id == appId)
            {
                data = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSuccessfulData(JsonElement entry, out JsonElement data)
    {
        data = default;
        return entry.ValueKind == JsonValueKind.Object &&
               entry.TryGetProperty("success", out var success) &&
               success.ValueKind == JsonValueKind.True &&
               entry.TryGetProperty("data", out data) &&
               data.ValueKind == JsonValueKind.Object;
    }
}
