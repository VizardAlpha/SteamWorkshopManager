using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SteamWorkshopManager.Services.Interfaces;

namespace SteamWorkshopManager.Services;

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJsonContext : JsonSerializerContext;

public class SettingsService : ISettingsService
{
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
            Console.WriteLine($"[ERROR] Failed to save settings: {ex.Message}");
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
            Console.WriteLine($"[ERROR] Failed to load settings: {ex.Message}");
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

    public string? GetContentFolderPath(ulong publishedFileId)
    {
        var key = publishedFileId.ToString();
        return Settings.ContentFolderPaths.TryGetValue(key, out var path) ? path : null;
    }

    public void SetContentFolderPath(ulong publishedFileId, string? path)
    {
        var key = publishedFileId.ToString();
        if (string.IsNullOrEmpty(path))
        {
            Settings.ContentFolderPaths.Remove(key);
        }
        else
        {
            Settings.ContentFolderPaths[key] = path;
        }
        Save();
    }

    public string? GetPreviewImagePath(ulong publishedFileId)
    {
        var key = publishedFileId.ToString();
        return Settings.PreviewImagePaths.TryGetValue(key, out var path) ? path : null;
    }

    public void SetPreviewImagePath(ulong publishedFileId, string? path)
    {
        var key = publishedFileId.ToString();
        if (string.IsNullOrEmpty(path))
        {
            Settings.PreviewImagePaths.Remove(key);
        }
        else
        {
            Settings.PreviewImagePaths[key] = path;
        }
        Save();
    }

    public IReadOnlyList<string> GetCustomTags() => Settings.CustomTags.AsReadOnly();

    public void AddCustomTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;

        var trimmed = tag.Trim();
        if (!Settings.CustomTags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            Settings.CustomTags.Add(trimmed);
            Save();
        }
    }

    public void RemoveCustomTag(string tag)
    {
        var index = Settings.CustomTags.FindIndex(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Settings.CustomTags.RemoveAt(index);
            Save();
        }
    }
}
