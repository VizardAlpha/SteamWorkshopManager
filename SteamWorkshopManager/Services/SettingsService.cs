using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services;

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJsonContext : JsonSerializerContext;

public class SettingsService : ISettingsService
{
    private static readonly Logger Log = LogService.GetLogger<SettingsService>();

    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamWorkshopManager"
    );

    private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

    public AppSettings Settings { get; private set; } = new();

    public SettingsService()
    {
        Load();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            var json = JsonSerializer.Serialize(Settings, SettingsJsonContext.Default.AppSettings);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save settings: {ex.Message}");
        }
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
                MigrateLanguageCodes();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load settings: {ex.Message}");
            Settings = new AppSettings();
        }
    }

    private void MigrateLanguageCodes()
    {
        var migrated = Settings.Language switch
        {
            "en" => "en-US",
            "fr" => "fr-FR",
            _ => null
        };

        if (migrated is not null)
        {
            Settings.Language = migrated;
            Save();
        }
    }

    public ItemFileInfo? GetContentFolderInfo(ulong publishedFileId)
    {
        var key = publishedFileId.ToString();
        return AppConfig.CurrentSession?.ContentFolderInfos.TryGetValue(key, out var info) == true ? info : null;
    }

    public void SetContentFolderPath(ulong publishedFileId, string? path)
    {
        var key = publishedFileId.ToString();
        var session = AppConfig.CurrentSession;
        if (session == null) return;

        if (string.IsNullOrEmpty(path))
            session.ContentFolderInfos.Remove(key);
        else
            session.ContentFolderInfos[key] = new ItemFileInfo { Path = path };

        SaveSessionAsync(session);
    }

    public void SetContentFolderInfo(ulong publishedFileId, ItemFileInfo? info)
    {
        var key = publishedFileId.ToString();
        var session = AppConfig.CurrentSession;
        if (session == null) return;

        if (info == null)
            session.ContentFolderInfos.Remove(key);
        else
            session.ContentFolderInfos[key] = info;

        SaveSessionAsync(session);
    }

    public ItemFileInfo? GetPreviewImageInfo(ulong publishedFileId)
    {
        var key = publishedFileId.ToString();
        return AppConfig.CurrentSession?.PreviewImageInfos.TryGetValue(key, out var info) == true ? info : null;
    }

    public void SetPreviewImagePath(ulong publishedFileId, string? path)
    {
        var key = publishedFileId.ToString();
        var session = AppConfig.CurrentSession;
        if (session == null) return;

        if (string.IsNullOrEmpty(path))
            session.PreviewImageInfos.Remove(key);
        else
            session.PreviewImageInfos[key] = new ItemFileInfo { Path = path };

        SaveSessionAsync(session);
    }

    public void SetPreviewImageInfo(ulong publishedFileId, ItemFileInfo? info)
    {
        var key = publishedFileId.ToString();
        var session = AppConfig.CurrentSession;
        if (session == null) return;

        if (info == null)
            session.PreviewImageInfos.Remove(key);
        else
            session.PreviewImageInfos[key] = info;

        SaveSessionAsync(session);
    }

    private static async void SaveSessionAsync(WorkshopSession session)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SteamWorkshopManager", "sessions"
            );
            var filePath = Path.Combine(folder, $"{session.Id}.json");
            var json = JsonSerializer.Serialize(session, SessionJsonContext.Default.WorkshopSession);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save session: {ex.Message}");
        }
    }

}
