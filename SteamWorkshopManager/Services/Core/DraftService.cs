using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Core;

/// <summary>
/// Persists in-progress mod drafts under <c>%AppData%/SteamWorkshopManager/Tempo</c>.
/// Layout: one sub-folder per draft, named with a compact Guid; each folder
/// holds a <c>draft.json</c> mirroring <see cref="CreateDraft"/>. Retrieval is
/// just "enumerate the sub-folders"; deletion is "recursively delete the
/// folder", which is what the Create view does after a successful publish.
/// </summary>
public sealed class DraftService
{
    private static readonly Logger Log = LogService.GetLogger<DraftService>();

    private static readonly string DraftsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamWorkshopManager",
        "tempo");

    private const string FileName = "draft.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Writes <paramref name="draft"/> to disk. If its <see cref="CreateDraft.TempId"/>
    /// is empty we mint a new one; otherwise the existing folder is overwritten
    /// (the update-in-place path). Returns the final TempId so the caller can
    /// re-bind its "current draft" state.
    /// </summary>
    public string Save(CreateDraft draft)
    {
        var tempId = string.IsNullOrEmpty(draft.TempId) ? Guid.NewGuid().ToString("N") : draft.TempId;
        var folder = Path.Combine(DraftsRoot, tempId);
        Directory.CreateDirectory(folder);

        var final = draft with { TempId = tempId, UpdatedAt = DateTime.UtcNow };
        var json = JsonSerializer.Serialize(final, JsonOptions);
        File.WriteAllText(Path.Combine(folder, FileName), json);

        Log.Info($"Draft saved: {tempId} (\"{final.DisplayName}\")");
        return tempId;
    }

    /// <summary>
    /// Returns all drafts belonging to <paramref name="appId"/>, most recent
    /// first. Bad/unreadable folders are skipped silently — a corrupted draft
    /// shouldn't block the rest from loading.
    /// </summary>
    public List<CreateDraft> ListForApp(uint appId)
    {
        if (!Directory.Exists(DraftsRoot)) return [];

        var results = new List<CreateDraft>();
        foreach (var folder in Directory.GetDirectories(DraftsRoot))
        {
            var draft = TryLoad(folder);
            if (draft is not null && draft.AppId == appId)
                results.Add(draft);
        }
        return results.OrderByDescending(d => d.UpdatedAt).ToList();
    }

    public CreateDraft? Load(string tempId) => TryLoad(Path.Combine(DraftsRoot, tempId));

    public void Delete(string tempId)
    {
        if (string.IsNullOrEmpty(tempId)) return;
        var folder = Path.Combine(DraftsRoot, tempId);
        if (!Directory.Exists(folder)) return;

        try
        {
            Directory.Delete(folder, recursive: true);
            Log.Info($"Draft deleted: {tempId}");
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to delete draft {tempId}: {ex.Message}");
        }
    }

    private static CreateDraft? TryLoad(string folder)
    {
        try
        {
            var path = Path.Combine(folder, FileName);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CreateDraft>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not load draft at {folder}: {ex.Message}");
            return null;
        }
    }
}
