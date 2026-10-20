using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SteamWorkshopManager.Core.Steam;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Workshop;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
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
    IReadOnlyList<AppDependencyInfo> AppDependencies,
    IReadOnlyList<TranslationChange> Translations);

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
    ItemTranslationService translationService,
    DraftService draftService,
    ISessionContext context)
{
    private static readonly Logger Log = LogService.GetLogger<WorkshopOrchestrator>();

    public async Task<WorkshopActionResult> PublishAsync(
        CreateModRequest request,
        IProgress<UploadProgress>? progress = null,
        string? draftIdToCleanup = null)
    {
        try
        {
            var outcome = await steamService.CreateItemAsync(
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

            if (outcome.FileId is not { } fileId)
            {
                // Steam's own reason when it gave one, so the user learns more than
                // "creation failed" (e.g. a Workshop closed to player submissions).
                notifications.ShowError(outcome.Result is { } result
                    ? SteamErrorMapper.GetCreateItemErrorMessage(result)
                    : LocalizationService.GetString("CreationFailed"));
                return new WorkshopActionResult(false, null, "CreationFailed", null);
            }

            telemetry.Track(TelemetryEventTypes.ModCreated, context.AppId);

            var id = (ulong)fileId;
            var folderInfo = await Task.Run(() => ModFileInfoBuilder.BuildForFolder(request.ContentFolderPath));
            if (folderInfo is not null) settingsService.SetContentFolderInfo(id, folderInfo);
            var imageInfo = ModFileInfoBuilder.BuildForFile(request.PreviewImagePath);
            if (imageInfo is not null) settingsService.SetPreviewImageInfo(id, imageInfo);

            foreach (var dep in request.Dependencies)
                await dependencyService.AddDependencyAsync(fileId, new PublishedFileId_t(dep.PublishedFileId));
            foreach (var appDep in request.AppDependencies)
                await appDependencyService.AddAppDependencyAsync(fileId, new AppId_t(appDep.AppId));

            // After the base submit, which carries the text the fallbacks use.
            var failedTranslations = request.Translations.Count > 0
                ? await UpdateTranslationsAsync(fileId, request.Translations, progress)
                : [];

            if (!string.IsNullOrEmpty(draftIdToCleanup))
                draftService.Delete(draftIdToCleanup);

            if (failedTranslations.Count > 0)
            {
                var names = failedTranslations.Select(code => SteamLanguages.Find(code)?.NativeName ?? code);
                notifications.ShowError(string.Format(
                    LocalizationService.GetString("TranslationsCreateFailed"), string.Join(", ", names)));
            }
            else
            {
                notifications.ShowSuccess(LocalizationService.GetString("ItemCreatedSuccess"));
            }
            return new WorkshopActionResult(true, fileId, null, null);
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
                var folderInfo = await Task.Run(() => ModFileInfoBuilder.BuildForFolder(request.ContentFolderPath));
                if (folderInfo is not null) settingsService.SetContentFolderInfo(id, folderInfo);
            }
            if (!string.IsNullOrEmpty(request.PreviewImagePath))
            {
                var imageInfo = ModFileInfoBuilder.BuildForFile(request.PreviewImagePath);
                if (imageInfo is not null) settingsService.SetPreviewImageInfo(id, imageInfo);
            }

            telemetry.Track(TelemetryEventTypes.ModUpdated, context.AppId);
            notifications.ShowSuccess(LocalizationService.GetString("ItemUpdatedSuccess"));
            return new WorkshopActionResult(true, request.FileId, null, null);
        }
        catch (Exception ex)
        {
            notifications.ShowError(LocalizationService.GetString("UpdateFailed"));
            return new WorkshopActionResult(false, request.FileId, "UpdateFailed", ex.Message);
        }
    }

    /// <summary>One submit per language, after the main update. Returns the languages that failed.</summary>
    public async Task<IReadOnlyList<string>> UpdateTranslationsAsync(
        PublishedFileId_t fileId,
        IReadOnlyList<TranslationChange> changes,
        IProgress<UploadProgress>? progress = null)
    {
        var failed = new List<string>();
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            progress?.Report(new UploadProgress("UploadingTranslations", 0, 0, i * 100.0 / changes.Count));
            try
            {
                if (!await translationService.UpdateTextAsync(fileId, change.Language, change.Text))
                    failed.Add(change.Language);
            }
            catch (Exception ex)
            {
                Log.Error($"Translation {change.Language} failed: {ex.Message}");
                failed.Add(change.Language);
            }
        }
        progress?.Report(new UploadProgress("Done", 0, 0, 100));
        return failed;
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
            telemetry.Track(TelemetryEventTypes.ModDeleted, context.AppId);
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
