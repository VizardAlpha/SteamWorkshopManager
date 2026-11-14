using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using WorkshopManager.Core.Workshop;
using WorkshopManager.Helpers;
using CommunityToolkit.Mvvm.Input;
using WorkshopManager.Services.Steam;
using WorkshopManager.Services.UI;

namespace WorkshopManager.ViewModels.Shell;

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

    /// <summary>The running upload can be stopped (mod.io uploads, bulk updates; see <see cref="UploadCancellation"/>).</summary>
    [ObservableProperty]
    private bool _canCancel;

    private readonly UploadCancellation _cancellation;

    /// <summary>Raised on the UI thread when an upload starts or ends.</summary>
    public event Action<bool>? InProgressChanged;

    public IProgress<UploadProgress> Reporter { get; }

    public UploadProgressViewModel(UploadCancellation cancellation)
    {
        _cancellation = cancellation;
        _cancellation.Changed += () => Dispatcher.UIThread.Post(() => CanCancel = _cancellation.CanCancel);
        // One ordered hop to the UI thread, whatever thread reports.
        Reporter = new DirectProgress<UploadProgress>(p => Dispatcher.UIThread.Post(() => Apply(p)));
    }

    [RelayCommand]
    private void Cancel() => _cancellation.Cancel();

    /// <summary>The worker died mid-upload and will never send its final report.</summary>
    public void Abort() => IsInProgress = false;

    partial void OnIsInProgressChanged(bool value) => InProgressChanged?.Invoke(value);

    /// <summary>A bulk update owns the toast until <see cref="EndBatch"/>: one mod finishing must not close it.</summary>
    public void ApplyBatch(BulkUpdateProgress p)
    {
        IsInProgress = true;
        StatusMessage = p.RetryIn is { } wait
            ? string.Format(Loc["BulkUpdateWaiting"], $"{(int)wait.TotalMinutes}:{wait.Seconds:00}")
            : string.Format(Loc["BulkUpdateStatus"], p.Index, p.Total, p.Title);
        Progress = p.BytesTotal > 0 ? p.BytesDone * 100.0 / p.BytesTotal : (p.Index - 1) * 100.0 / p.Total;
        var left = string.Format(Loc["BulkUpdateLeft"], Formatters.Bytes(Math.Max(0, p.BytesTotal - p.BytesDone)));
        ProgressText = $"{Formatters.Bytes(p.BytesDone)} / {Formatters.Bytes(p.BytesTotal)} ({Progress:F0}%) · {left}";
    }

    public void EndBatch() => IsInProgress = false;

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
