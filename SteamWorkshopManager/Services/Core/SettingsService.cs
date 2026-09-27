using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;

namespace SteamWorkshopManager.Services.Core;

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJsonContext : JsonSerializerContext;

public class SettingsService : ISettingsService
{
    private static readonly Logger Log = LogService.GetLogger<SettingsService>();

    private static readonly string SettingsFolder = AppPaths.Root;

    private static readonly string SettingsPath = AppPaths.SettingsFile;

    public AppSettings Settings { get; private set; } = new();

    private readonly ISessionContext _context;

    public SettingsService(ISessionContext context)
    {
        _context = context;
        Load();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            var json = JsonSerializer.Serialize(Settings, SettingsJsonContext.Default.AppSettings);
            AtomicFile.WriteAllText(SettingsPath, json);
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
        return _context.Current?.ContentFolderInfos.TryGetValue(key, out var info) == true ? info : null;
    }

    public void SetContentFolderPath(ulong publishedFileId, string? path)
    {
        var key = publishedFileId.ToString();
        var session = _context.Current;
        if (session == null) return;

        if (string.IsNullOrEmpty(path))
            session.ContentFolderInfos.Remove(key);
        else
            session.ContentFolderInfos[key] = new ItemFileInfo { Path = path };

        SaveSession(session);
    }

    public void SetContentFolderInfo(ulong publishedFileId, ItemFileInfo? info)
    {
        var key = publishedFileId.ToString();
        var session = _context.Current;
        if (session == null) return;

        if (info == null)
            session.ContentFolderInfos.Remove(key);
        else
            session.ContentFolderInfos[key] = info;

        SaveSession(session);
    }

    public ItemFileInfo? GetPreviewImageInfo(ulong publishedFileId)
    {
        var key = publishedFileId.ToString();
        return _context.Current?.PreviewImageInfos.TryGetValue(key, out var info) == true ? info : null;
    }

    public void SetPreviewImagePath(ulong publishedFileId, string? path)
    {
        var key = publishedFileId.ToString();
        var session = _context.Current;
        if (session == null) return;

        if (string.IsNullOrEmpty(path))
            session.PreviewImageInfos.Remove(key);
        else
            session.PreviewImageInfos[key] = new ItemFileInfo { Path = path };

        SaveSession(session);
    }

    public void SetPreviewImageInfo(ulong publishedFileId, ItemFileInfo? info)
    {
        var key = publishedFileId.ToString();
        var session = _context.Current;
        if (session == null) return;

        if (info == null)
            session.PreviewImageInfos.Remove(key);
        else
            session.PreviewImageInfos[key] = info;

        SaveSession(session);
    }

    // Synchronous on purpose: snapshotting at call time keeps back-to-back saves in order.
    private static void SaveSession(WorkshopSession session)
    {
        try
        {
            var filePath = AppPaths.SessionFile(session.Id);
            var json = JsonSerializer.Serialize(session, SessionJsonContext.Default.WorkshopSession);
            AtomicFile.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save session: {ex.Message}");
        }
    }

}
