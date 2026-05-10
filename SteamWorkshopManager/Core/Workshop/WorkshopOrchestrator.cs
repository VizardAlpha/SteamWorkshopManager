using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Telemetry;
using Steamworks;

namespace SteamWorkshopManager.Core.Workshop;

/// <summary>Outcome of a publish/update/delete flow surfaced to the VM.</summary>
public sealed record WorkshopActionResult(
    bool Success,
    PublishedFileId_t? FileId,
    string? ErrorKey,
    string? ExceptionMessage);

/// <summary>Inputs for <see cref="WorkshopOrchestrator.PublishAsync"/>.</summary>
public sealed record CreateModRequest(
    string Title,
    string Description,
    string ContentFolderPath,
    string? PreviewImagePath,
    VisibilityType Visibility,
    List<string> Tags,
    string? InitialChangelog,
    string? BranchMin,
    string? BranchMax,
    IReadOnlyList<PreviewOp>? PreviewOps,
    IReadOnlyList<DependencyInfo> Dependencies,
    IReadOnlyList<AppDependencyInfo> AppDependencies);

/// <summary>Inputs for <see cref="WorkshopOrchestrator.UpdateAsync"/>. Each
/// field is null when unchanged so Steam only re-uploads what actually moved.</summary>
public sealed record UpdateModRequest(
    PublishedFileId_t FileId,
    string? Title,
    string? Description,
    string? ContentFolderPath,
    string? PreviewImagePath,
    VisibilityType? Visibility,
    List<string>? Tags,
    string? Changelog,
    string? BranchMin,
    string? BranchMax,
    IReadOnlyList<PreviewOp>? PreviewOps);

/// <summary>
/// High-level façade that wraps the Create/Update/Delete flows: validation
/// has already happened in the VM (see <see cref="ModValidator"/>), this
/// stage drives the Steam call, side effects (file info persistence,
/// telemetry, dependency wiring, draft cleanup), and surfaces the user
/// notification. The VM stays a thin layer of UI state.
/// </summary>
public sealed class WorkshopOrchestrator(
    ISteamService steamService,
    ISettingsService settingsService,
    INotificationService notifications,
    ITelemetryService telemetry,
    DependencyService dependencyService,
    AppDependencyService appDependencyService,
    DraftService draftService)
{
    public async Task<WorkshopActionResult> PublishAsync(
        CreateModRequest request,
        IProgress<UploadProgress>? progress = null,
        string? draftIdToCleanup = null)
    {
        try
        {
            var fileId = await steamService.CreateItemAsync(
                request.Title,
                request.Description,
                request.ContentFolderPath,
                request.PreviewImagePath,
                request.Visibility,
                request.Tags,
                string.IsNullOrWhiteSpace(request.InitialChangelog) ? "Initial version" : request.InitialChangelog,
                progress,
                request.BranchMin,
                request.BranchMax,
                request.PreviewOps);

            if (!fileId.HasValue)
            {
                notifications.ShowError(LocalizationService.GetString("CreationFailed"));
                return new WorkshopActionResult(false, null, "CreationFailed", null);
            }

            telemetry.Track(TelemetryEventTypes.ModCreated, AppConfig.AppId);

            var id = (ulong)fileId.Value;
            var folderInfo = ModFileInfoBuilder.BuildForFolder(request.ContentFolderPath);
            if (folderInfo is not null) settingsService.SetContentFolderInfo(id, folderInfo);
            var imageInfo = ModFileInfoBuilder.BuildForFile(request.PreviewImagePath);
            if (imageInfo is not null) settingsService.SetPreviewImageInfo(id, imageInfo);

            foreach (var dep in request.Dependencies)
                await dependencyService.AddDependencyAsync(fileId.Value, new PublishedFileId_t(dep.PublishedFileId));
            foreach (var appDep in request.AppDependencies)
                await appDependencyService.AddAppDependencyAsync(fileId.Value, new AppId_t(appDep.AppId));

            if (!string.IsNullOrEmpty(draftIdToCleanup))
                draftService.Delete(draftIdToCleanup);

            notifications.ShowSuccess(LocalizationService.GetString("ItemCreatedSuccess"));
            return new WorkshopActionResult(true, fileId.Value, null, null);
        }
        catch (Exception ex)
        {
            notifications.ShowError(LocalizationService.GetString("CreationFailed"));
            return new WorkshopActionResult(false, null, "CreationFailed", ex.Message);
        }
    }

    public async Task<WorkshopActionResult> UpdateAsync(
        UpdateModRequest request,
        IProgress<UploadProgress>? progress = null)
    {
        try
        {
            var success = await steamService.UpdateItemAsync(
                request.FileId,
                request.Title,
                request.Description,
                request.ContentFolderPath,
                request.PreviewImagePath,
                request.Visibility,
                request.Tags,
                request.Changelog,
                progress,
                request.BranchMin,
                request.BranchMax,
                request.PreviewOps);

            if (!success)
            {
                notifications.ShowError(LocalizationService.GetString("UpdateFailed"));
                return new WorkshopActionResult(false, request.FileId, "UpdateFailed", null);
            }

            var id = (ulong)request.FileId;
            if (!string.IsNullOrEmpty(request.ContentFolderPath))
            {
                var folderInfo = ModFileInfoBuilder.BuildForFolder(request.ContentFolderPath);
                if (folderInfo is not null) settingsService.SetContentFolderInfo(id, folderInfo);
            }
            if (!string.IsNullOrEmpty(request.PreviewImagePath))
            {
                var imageInfo = ModFileInfoBuilder.BuildForFile(request.PreviewImagePath);
                if (imageInfo is not null) settingsService.SetPreviewImageInfo(id, imageInfo);
            }

            telemetry.Track(TelemetryEventTypes.ModUpdated, AppConfig.AppId);
            notifications.ShowSuccess(LocalizationService.GetString("ItemUpdatedSuccess"));
            return new WorkshopActionResult(true, request.FileId, null, null);
        }
        catch (Exception ex)
        {
            notifications.ShowError(LocalizationService.GetString("UpdateFailed"));
            return new WorkshopActionResult(false, request.FileId, "UpdateFailed", ex.Message);
        }
    }

    public async Task<WorkshopActionResult> DeleteAsync(PublishedFileId_t fileId)
    {
        try
        {
            var success = await steamService.DeleteItemAsync(fileId);
            if (!success)
            {
                notifications.ShowError(LocalizationService.GetString("DeleteFailed"));
                return new WorkshopActionResult(false, fileId, "DeleteFailed", null);
            }

            var id = (ulong)fileId;
            settingsService.SetContentFolderInfo(id, null);
            settingsService.SetPreviewImageInfo(id, null);
            telemetry.Track(TelemetryEventTypes.ModDeleted, AppConfig.AppId);
            notifications.ShowSuccess(LocalizationService.GetString("ItemDeletedSuccess"));
            return new WorkshopActionResult(true, fileId, null, null);
        }
        catch (Exception ex)
        {
            notifications.ShowError(LocalizationService.GetString("DeleteFailed"));
            return new WorkshopActionResult(false, fileId, "DeleteFailed", ex.Message);
        }
    }
}
