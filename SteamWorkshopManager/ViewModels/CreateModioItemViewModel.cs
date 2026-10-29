using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Platforms.Modio;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.ViewModels.Modio;

namespace SteamWorkshopManager.ViewModels;

public enum ModioCreateStep { Details, Media, File }

/// <summary>
/// Creates a mod.io mod the way mod.io's site does: details create the mod, then media and
/// the first file are added, each step skippable. The mod exists from the end of step 1.
/// </summary>
public partial class CreateModioItemViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger Log = LogService.GetLogger<CreateModioItemViewModel>();

    private readonly ModioPublisher _publisher;
    private readonly INotificationService _notifications;
    private readonly IProgress<UploadProgress>? _uploadProgress;
    private string? _profileUrl;
    private bool _finished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsActive), nameof(IsMediaActive), nameof(IsFileActive))]
    private ModioCreateStep _activeStep = ModioCreateStep.Details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModCreated), nameof(HasProfileUrl))]
    private ulong? _createdModId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateModCommand), nameof(SaveMediaCommand), nameof(UploadFileCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isMediaDone;

    [ObservableProperty]
    private string? _errorMessage;

    public ModioDetailsViewModel Details { get; }
    public ModioMediaViewModel Media { get; }
    public ModioFileViewModel File { get; }

    public bool IsDetailsActive => ActiveStep == ModioCreateStep.Details;
    public bool IsMediaActive => ActiveStep == ModioCreateStep.Media;
    public bool IsFileActive => ActiveStep == ModioCreateStep.File;

    public bool IsModCreated => CreatedModId is not null;

    public bool HasProfileUrl => IsModCreated && !string.IsNullOrEmpty(_profileUrl);

    /// <summary>Raised with the new mod's id when the user leaves the flow through Finish.</summary>
    public event Action<ulong>? ItemCreated;

    public CreateModioItemViewModel(
        ModioPublisher publisher,
        INotificationService notifications,
        ModioDetailsViewModel details,
        ModioMediaViewModel media,
        ModioFileViewModel file,
        IProgress<UploadProgress>? uploadProgress = null)
    {
        _publisher = publisher;
        _notifications = notifications;
        _uploadProgress = uploadProgress;
        Details = details;
        Media = media;
        File = file;
        Details.IsCreating = true;
        Details.Tags.LoadFromSession();
    }

    /// <summary>Created but left before Finish: the shell still has to show it in the list.</summary>
    public bool LeftUnfinished => IsModCreated && !_finished;

    [RelayCommand] private void NavigateToDetails() => ActiveStep = ModioCreateStep.Details;
    [RelayCommand] private void NavigateToMedia() { if (IsModCreated) ActiveStep = ModioCreateStep.Media; }
    [RelayCommand] private void NavigateToFile() { if (IsModCreated) ActiveStep = ModioCreateStep.File; }

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task CreateModAsync()
    {
        if (IsModCreated)
        {
            ActiveStep = ModioCreateStep.Media;
            return;
        }

        var d = Details;
        if (d.Validate() is { } errorKey)
        {
            ErrorMessage = Loc[errorKey];
            return;
        }

        var mod = new ModioNewMod(
            d.Title.Trim(), d.Summary.Trim(), d.LogoPath!,
            string.IsNullOrWhiteSpace(d.Description) ? null : d.Description,
            d.IsPublic, d.Tags.SelectedNames,
            string.IsNullOrWhiteSpace(d.HomepageUrl) ? null : d.HomepageUrl.Trim(),
            d.CommunityOptions(ModioCommunity.Default));

        await RunAsync("ModioCreateFailed", async () =>
        {
            var created = await _publisher.CreateAsync(mod);
            _profileUrl = created.ProfileUrl;
            CreatedModId = (ulong)created.Id;
            d.IsLocked = true;
            _notifications.ShowSuccess(Loc["ModioModCreated"]);
            ActiveStep = ModioCreateStep.Media;
        });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SaveMediaAsync()
    {
        if (CreatedModId is not { } id) return;

        var change = Media.BuildChange();
        if (!change.HasAdditions)
        {
            ActiveStep = ModioCreateStep.File;
            return;
        }

        await RunAsync("ModioMediaFailed", async () =>
        {
            await _publisher.UpdateMediaAsync(id, change, _uploadProgress);
            IsMediaDone = true;
            ActiveStep = ModioCreateStep.File;
        });
    }

    [RelayCommand]
    private void SkipMedia() => ActiveStep = ModioCreateStep.File;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task UploadFileAsync()
    {
        if (CreatedModId is not { } id) return;
        if (File.ToUpload() is not { } upload)
        {
            ErrorMessage = Loc["FolderRequired"];
            return;
        }

        await RunAsync("ModioFileFailed", async () =>
        {
            await _publisher.UploadFileAsync(id, upload, _uploadProgress);
            Finish();
        });
    }

    /// <summary>Leaves without a file: the mod stays on mod.io, not playable until one is added.</summary>
    [RelayCommand]
    private void FinishWithoutFile() => Finish();

    [RelayCommand]
    private void OpenOnModio()
    {
        if (!HasProfileUrl) return;
        try { Process.Start(new ProcessStartInfo(_profileUrl!) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Debug($"Failed to open the mod page: {ex.Message}"); }
    }

    private void Finish()
    {
        if (CreatedModId is not { } id) return;
        _finished = true;
        ItemCreated?.Invoke(id);
    }

    private async Task RunAsync(string failureKey, Func<Task> step)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await step();
        }
        catch (ModioApiException ex)
        {
            ErrorMessage = ModioErrors.Describe(ex, failureKey);
        }
        catch (Exception ex)
        {
            Log.Error("mod.io create step failed", ex);
            ErrorMessage = $"{Loc[failureKey]}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        Details.Dispose();
        Media.Dispose();
        File.Dispose();
    }
}
