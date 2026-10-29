using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Steam;

public sealed record SteamCredentials(string? RefreshToken, string? AccessToken, string? AccountName, ulong SteamId64)
{
    public bool IsEmpty => string.IsNullOrEmpty(RefreshToken) && string.IsNullOrEmpty(AccessToken)
                           && string.IsNullOrEmpty(AccountName) && SteamId64 == 0;
}

[JsonSerializable(typeof(SteamCredentials))]
internal partial class SteamCredentialsJsonContext : JsonSerializerContext;

/// <summary>
/// Persists Steam login tokens outside settings.json, in the machine-local folder
/// through <see cref="ProtectedFile"/>.
/// </summary>
public static class SteamCredentialStore
{
    private static readonly Logger Log = new(nameof(SteamCredentialStore), LogService.Instance);

    // Binds the DPAPI blob to this app so another app's Unprotect call with no entropy can't read it.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SteamWorkshopManager.SteamCredentials.v1");

    public static string FilePath { get; set; } = AppPaths.CredentialsFile;

    public static SteamCredentials? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = ProtectedFile.Read(FilePath, Entropy);
            return JsonSerializer.Deserialize(json, SteamCredentialsJsonContext.Default.SteamCredentials);
        }
        catch (Exception ex)
        {
            // Unreadable (other user, copied from another machine, corrupted): treat as signed out.
            Log.Warning($"Could not read stored Steam credentials: {ex.GetType().Name}");
            return null;
        }
    }

    public static void Save(SteamCredentials credentials)
    {
        if (credentials.IsEmpty)
        {
            Delete();
            return;
        }

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(credentials, SteamCredentialsJsonContext.Default.SteamCredentials);
            ProtectedFile.Write(FilePath, json, Entropy);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save Steam credentials: {ex.Message}");
        }
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to delete Steam credentials: {ex.Message}");
        }
    }
}
