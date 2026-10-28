using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.ViewModels.Shell;

/// <summary>Upload toast fed by the progress the worker streams back.</summary>
public partial class UploadProgressViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isInProgress;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    /// <summary>Raised on the UI thread when an upload starts or ends.</summary>
    public event Action<bool>? InProgressChanged;

    public IProgress<UploadProgress> Reporter { get; }

    public UploadProgressViewModel()
    {
        Reporter = new Progress<UploadProgress>(p => Dispatcher.UIThread.Post(() => Apply(p)));
    }

    /// <summary>The worker died mid-upload and will never send its final report.</summary>
    public void Abort() => IsInProgress = false;

    partial void OnIsInProgressChanged(bool value) => InProgressChanged?.Invoke(value);

    private void Apply(UploadProgress p)
    {
        IsInProgress = p.Percentage < 100;
        // The worker sends localization keys.
        StatusMessage = string.IsNullOrEmpty(p.Status) ? string.Empty : Loc[p.Status];
        Progress = p.Percentage;
        ProgressText = p.BytesTotal > 0
            ? $"{Formatters.Bytes((long)p.BytesProcessed)} / {Formatters.Bytes((long)p.BytesTotal)} ({p.Percentage:F0}%)"
            : $"{p.Percentage:F0}%";
    }
}
