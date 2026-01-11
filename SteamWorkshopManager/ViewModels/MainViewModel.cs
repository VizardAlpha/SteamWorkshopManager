using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services;

namespace SteamWorkshopManager.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ISteamService _steamService;
    private readonly IFileDialogService _fileDialogService;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private bool _isSteamConnected;

    [ObservableProperty]
    private string _statusMessage = "Connexion à Steam...";

    [ObservableProperty]
    private WorkshopItem? _selectedItem;

    [ObservableProperty]
    private bool _isUploadInProgress;

    [ObservableProperty]
    private string _uploadStatusMessage = string.Empty;

    [ObservableProperty]
    private double _uploadProgress;

    [ObservableProperty]
    private string _uploadProgressText = string.Empty;

    public ItemListViewModel ItemListViewModel { get; }

    public IProgress<UploadProgress> UploadProgressReporter { get; }

    public MainViewModel() : this(new SteamService(), new FileDialogService()) { }

    public MainViewModel(ISteamService steamService, IFileDialogService fileDialogService)
    {
        _steamService = steamService;
        _fileDialogService = fileDialogService;

        // Configurer le reporter de progression
        UploadProgressReporter = new Progress<UploadProgress>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsUploadInProgress = p.Percentage < 100;
                UploadStatusMessage = p.Status;
                UploadProgress = p.Percentage;

                if (p.BytesTotal > 0)
                {
                    var processed = FormatBytes(p.BytesProcessed);
                    var total = FormatBytes(p.BytesTotal);
                    UploadProgressText = $"{processed} / {total} ({p.Percentage:F0}%)";
                }
                else
                {
                    UploadProgressText = $"{p.Percentage:F0}%";
                }
            });
        });

        ItemListViewModel = new ItemListViewModel(_steamService);
        ItemListViewModel.ItemSelected += OnItemSelected;
        ItemListViewModel.CreateRequested += OnCreateRequested;

        CurrentView = ItemListViewModel;

        InitializeSteamAsync();
    }

    private static string FormatBytes(ulong bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
        };
    }

    private async void InitializeSteamAsync()
    {
        var connected = await Task.Run(() => _steamService.Initialize());

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            IsSteamConnected = connected;

            if (connected)
            {
                StatusMessage = "Connecté à Steam";
                await ItemListViewModel.LoadItemsAsync();
            }
            else
            {
                StatusMessage = "Steam non disponible - Lancez Steam et redémarrez l'application";
            }
        });
    }

    private void OnItemSelected(WorkshopItem item)
    {
        SelectedItem = item;
        CurrentView = new ItemEditorViewModel(item, _steamService, _fileDialogService, UploadProgressReporter);
        if (CurrentView is ItemEditorViewModel editor)
        {
            editor.CloseRequested += OnEditorCloseRequested;
            editor.ItemDeleted += OnItemDeleted;
        }
    }

    private void OnCreateRequested()
    {
        CurrentView = new CreateItemViewModel(_steamService, _fileDialogService, UploadProgressReporter);
        if (CurrentView is CreateItemViewModel creator)
        {
            creator.CloseRequested += OnEditorCloseRequested;
            creator.ItemCreated += OnItemCreated;
        }
    }

    private void OnEditorCloseRequested()
    {
        CurrentView = ItemListViewModel;
        SelectedItem = null;
    }

    private async void OnItemDeleted()
    {
        CurrentView = ItemListViewModel;
        SelectedItem = null;
        await ItemListViewModel.LoadItemsAsync();
    }

    private async void OnItemCreated()
    {
        CurrentView = ItemListViewModel;
        await ItemListViewModel.LoadItemsAsync();
    }

    [RelayCommand]
    private void NavigateToList()
    {
        CurrentView = ItemListViewModel;
        SelectedItem = null;
    }

    [RelayCommand]
    private void NavigateToCreate()
    {
        OnCreateRequested();
    }
}
