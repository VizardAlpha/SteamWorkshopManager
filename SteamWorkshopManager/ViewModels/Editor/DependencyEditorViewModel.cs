using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Workshop;
using SteamWorkshopManager.Services.Notifications;
using Steamworks;

namespace SteamWorkshopManager.ViewModels.Editor;

/// <summary>
/// Required mods and apps. Staged mode (create view) only edits the lists, the
/// orchestrator applies them after publishing. Live mode (edit view, after
/// <see cref="AttachTo"/>) applies every add/remove on Steam right away.
/// </summary>
public partial class DependencyEditorViewModel(
    DependencyService dependencyService,
    AppDependencyService appDependencyService,
    INotificationService notifications,
    ISessionContext context) : ViewModelBase
{
    private PublishedFileId_t? _owner;

    public ObservableCollection<DependencyInfo> Dependencies { get; } = [];
    public ObservableCollection<AppDependencyInfo> AppDependencies { get; } = [];

    [ObservableProperty]
    private bool _isLoadingDependencies;

    [ObservableProperty]
    private string _newDependencyInput = string.Empty;

    [ObservableProperty]
    private DependencyInfo? _previewDependency;

    [ObservableProperty]
    private bool _isSearchingDependency;

    [ObservableProperty]
    private bool _isAddingDependency;

    [ObservableProperty]
    private string? _dependencyError;

    [ObservableProperty]
    private string _newAppIdInput = string.Empty;

    [ObservableProperty]
    private AppDependencyInfo? _appPreviewInfo;

    [ObservableProperty]
    private bool _isSearchingApp;

    [ObservableProperty]
    private bool _isAddingApp;

    [ObservableProperty]
    private string? _addAppError;

    public bool IsLoaded { get; private set; }

    /// <summary>Switches to live mode for an existing item.</summary>
    public void AttachTo(PublishedFileId_t owner) => _owner = owner;

    [RelayCommand]
    private async Task LoadDependenciesAsync()
    {
        if (_owner is not { } owner) return;

        IsLoadingDependencies = true;
        DependencyError = null;
        try
        {
            var deps = await dependencyService.GetDependenciesAsync(owner);
            Dependencies.Clear();
            foreach (var dep in deps) Dependencies.Add(dep);

            var appDeps = await appDependencyService.GetAppDependenciesAsync(owner);
            AppDependencies.Clear();
            foreach (var appDep in appDeps) AppDependencies.Add(appDep);

            IsLoaded = true;
        }
        catch (Exception ex)
        {
            DependencyError = ex.Message;
        }
        finally
        {
            IsLoadingDependencies = false;
        }
    }

    // ─── Workshop items ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SearchDependencyAsync()
    {
        DependencyError = null;
        PreviewDependency = null;

        var fileId = WorkshopInputParser.ParseWorkshopId(NewDependencyInput);
        if (fileId == 0)
        {
            DependencyError = Loc["InvalidWorkshopInput"];
            return;
        }
        if (_owner is { } owner && fileId == owner.m_PublishedFileId)
        {
            DependencyError = Loc["CannotAddSelf"];
            return;
        }
        if (Dependencies.Any(d => d.PublishedFileId == fileId))
        {
            DependencyError = Loc["DependencyAlreadyExists"];
            return;
        }

        IsSearchingDependency = true;
        try
        {
            var info = await dependencyService.GetModDetailsAsync(new PublishedFileId_t(fileId));
            if (info == null || !info.IsValid)
            {
                DependencyError = Loc["WorkshopItemNotFound"];
                return;
            }
            PreviewDependency = info;
        }
        catch (Exception ex)
        {
            DependencyError = ex.Message;
        }
        finally
        {
            IsSearchingDependency = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAddDependencyAsync()
    {
        if (PreviewDependency is not { } dep) return;

        DependencyError = null;
        if (_owner is { } owner)
        {
            IsAddingDependency = true;
            try
            {
                if (!await dependencyService.AddDependencyAsync(owner, new PublishedFileId_t(dep.PublishedFileId)))
                {
                    DependencyError = Loc["AddDependencyFailed"];
                    return;
                }
                notifications.ShowSuccess(Loc["DependencyAdded"]);
            }
            catch (Exception ex)
            {
                DependencyError = $"{Loc["AddDependencyFailed"]}: {ex.Message}";
                return;
            }
            finally
            {
                IsAddingDependency = false;
            }
        }

        Dependencies.Add(dep);
        PreviewDependency = null;
        NewDependencyInput = string.Empty;
    }

    [RelayCommand]
    private void CancelDependencyPreview()
    {
        PreviewDependency = null;
        DependencyError = null;
    }

    [RelayCommand]
    private async Task RemoveDependencyAsync(DependencyInfo? dep)
    {
        if (dep == null) return;

        if (_owner is { } owner)
        {
            dep.IsRemoving = true;
            DependencyError = null;
            try
            {
                if (!await dependencyService.RemoveDependencyAsync(owner, new PublishedFileId_t(dep.PublishedFileId)))
                {
                    DependencyError = Loc["RemoveDependencyFailed"];
                    return;
                }
                notifications.ShowSuccess(Loc["DependencyRemoved"]);
            }
            catch (Exception ex)
            {
                DependencyError = $"{Loc["RemoveDependencyFailed"]}: {ex.Message}";
                return;
            }
            finally
            {
                dep.IsRemoving = false;
            }
        }

        Dependencies.Remove(dep);
    }

    [RelayCommand]
    private void MoveDependencyUp(DependencyInfo dep)
    {
        var index = Dependencies.IndexOf(dep);
        if (index > 0) Dependencies.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveDependencyDown(DependencyInfo dep)
    {
        var index = Dependencies.IndexOf(dep);
        if (index >= 0 && index < Dependencies.Count - 1) Dependencies.Move(index, index + 1);
    }

    [RelayCommand]
    private static void OpenDependencyInBrowser(DependencyInfo? dep)
    {
        if (dep == null) return;
        Process.Start(new ProcessStartInfo { FileName = dep.WorkshopUrl, UseShellExecute = true });
    }

    // ─── Apps (DLC / tools) ───────────────────────────────────────────────────

    [RelayCommand]
    private async Task SearchAppAsync()
    {
        AddAppError = null;
        AppPreviewInfo = null;

        if (!AppIdValidator.TryParseAppId(NewAppIdInput, out var appId))
        {
            AddAppError = Loc["InvalidAppId"];
            return;
        }
        if (appId == context.AppId)
        {
            AddAppError = Loc["CannotAddOwnGame"];
            return;
        }
        if (AppDependencies.Any(d => d.AppId == appId))
        {
            AddAppError = Loc["AppDependencyAlreadyExists"];
            return;
        }

        IsSearchingApp = true;
        try
        {
            var name = await appDependencyService.ResolveAppNameAsync(appId);
            if (name == null)
            {
                AddAppError = Loc["AppNotFound"];
                return;
            }
            AppPreviewInfo = new AppDependencyInfo { AppId = appId, Name = name };
        }
        catch (Exception ex)
        {
            AddAppError = ex.Message;
        }
        finally
        {
            IsSearchingApp = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAddAppAsync()
    {
        if (AppPreviewInfo is not { } app) return;

        AddAppError = null;
        if (_owner is { } owner)
        {
            IsAddingApp = true;
            try
            {
                if (!await appDependencyService.AddAppDependencyAsync(owner, new AppId_t(app.AppId)))
                {
                    AddAppError = Loc["AddAppDependencyFailed"];
                    return;
                }
                notifications.ShowSuccess(Loc["AppDependencyAdded"]);
            }
            catch (Exception ex)
            {
                AddAppError = $"{Loc["AddAppDependencyFailed"]}: {ex.Message}";
                return;
            }
            finally
            {
                IsAddingApp = false;
            }
        }

        AppDependencies.Add(app);
        AppPreviewInfo = null;
        NewAppIdInput = string.Empty;
    }

    [RelayCommand]
    private void CancelAppPreview()
    {
        AppPreviewInfo = null;
        AddAppError = null;
    }

    [RelayCommand]
    private async Task RemoveAppDependencyAsync(AppDependencyInfo? dep)
    {
        if (dep == null) return;

        if (_owner is { } owner)
        {
            dep.IsRemoving = true;
            AddAppError = null;
            try
            {
                if (!await appDependencyService.RemoveAppDependencyAsync(owner, new AppId_t(dep.AppId)))
                {
                    AddAppError = Loc["RemoveAppDependencyFailed"];
                    return;
                }
                notifications.ShowSuccess(Loc["AppDependencyRemoved"]);
            }
            catch (Exception ex)
            {
                AddAppError = $"{Loc["RemoveAppDependencyFailed"]}: {ex.Message}";
                return;
            }
            finally
            {
                dep.IsRemoving = false;
            }
        }

        AppDependencies.Remove(dep);
    }

    [RelayCommand]
    private static void OpenAppInStore(AppDependencyInfo? dep)
    {
        if (dep == null) return;
        Process.Start(new ProcessStartInfo { FileName = dep.StoreUrl, UseShellExecute = true });
    }
}
