using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Workshop;
using SteamWorkshopManager.Services.Log;
using Steamworks;

namespace SteamWorkshopManager.ViewModels.Editor;

/// <summary>Game-branch targeting (min/max beta range) shared by the create and edit views.</summary>
public partial class VersionRangeViewModel(VersioningService versioningService) : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<VersionRangeViewModel>();

    [ObservableProperty]
    private bool _isVersioningEnabled;

    [ObservableProperty]
    private bool _isLoadingVersions;

    [ObservableProperty]
    private string _currentBranch = string.Empty;

    [ObservableProperty]
    private bool _targetAllVersions = true;

    [ObservableProperty]
    private GameBranch? _selectedBranchMin;

    [ObservableProperty]
    private GameBranch? _selectedBranchMax;

    [ObservableProperty]
    private bool _isBranchRangeInvalid;

    [ObservableProperty]
    private List<GameBranch> _availableBranches = [];

    /// <summary>Ranges already published for the edited item (edit view only).</summary>
    public ObservableCollection<ModVersionInfo> ExistingVersions { get; } = [];

    partial void OnSelectedBranchMinChanged(GameBranch? value) => ValidateBranchRange();
    partial void OnSelectedBranchMaxChanged(GameBranch? value) => ValidateBranchRange();

    /// <summary>Clears the selection, e.g. after a session switch.</summary>
    public void Reset()
    {
        SelectedBranchMin = null;
        SelectedBranchMax = null;
        TargetAllVersions = true;
        IsBranchRangeInvalid = false;
    }

    /// <summary>Branches come from the worker over RPC: loaded asynchronously, never on the UI thread.</summary>
    public async Task LoadBranchesAsync()
    {
        try
        {
            IsVersioningEnabled = await versioningService.IsVersioningEnabledAsync();
            if (IsVersioningEnabled)
            {
                CurrentBranch = await versioningService.GetCurrentBranchAsync();
                AvailableBranches = await versioningService.GetAvailableBranchesAsync();
            }
            else
            {
                CurrentBranch = string.Empty;
                AvailableBranches = [];
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Loading game branches failed: {ex.Message}");
        }
    }

    public async Task LoadExistingVersionsAsync(PublishedFileId_t fileId)
    {
        var versions = await versioningService.GetModVersionsAsync(fileId);
        ExistingVersions.Clear();
        foreach (var v in versions)
            ExistingVersions.Add(v);
    }

    public void SelectByName(string? min, string? max)
    {
        SelectedBranchMin = AvailableBranches.FirstOrDefault(b => b.Name == min);
        SelectedBranchMax = AvailableBranches.FirstOrDefault(b => b.Name == max);
    }

    /// <summary>Branch range to send with an upload; nulls when every version is targeted.</summary>
    public (string? Min, string? Max) GetRange() =>
        IsVersioningEnabled && !TargetAllVersions
            ? (SelectedBranchMin?.Name, SelectedBranchMax?.Name)
            : (null, null);

    private void ValidateBranchRange()
    {
        if (SelectedBranchMin == null || SelectedBranchMax == null)
        {
            IsBranchRangeInvalid = false;
            return;
        }
        IsBranchRangeInvalid = AvailableBranches.IndexOf(SelectedBranchMin) > AvailableBranches.IndexOf(SelectedBranchMax);
    }
}
