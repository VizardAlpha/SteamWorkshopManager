using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using Steamworks;

namespace SteamWorkshopManager.Services.Interfaces;

public interface ISteamService
{
    bool IsInitialized { get; }
    CSteamID? CurrentUserId { get; }

    bool Initialize();
    void Shutdown();

    Task<List<WorkshopItem>> GetPublishedItemsAsync();
    Task<PublishedFileId_t?> CreateItemAsync(string title, string description, string contentFolderPath,
        string? previewImagePath, VisibilityType visibility, List<string> tags, string? changelog,
        IProgress<UploadProgress>? progress = null);
    Task<bool> UpdateItemAsync(PublishedFileId_t fileId, string? title, string? description,
        string? contentFolderPath, string? previewImagePath, VisibilityType? visibility,
        List<string>? tags, string? changelog, IProgress<UploadProgress>? progress = null);
    Task<bool> DeleteItemAsync(PublishedFileId_t fileId);
    
}

public record UploadProgress(string Status, ulong BytesProcessed, ulong BytesTotal)
{
    public double Percentage => BytesTotal > 0 ? (double)BytesProcessed / BytesTotal * 100 : 0;
}
