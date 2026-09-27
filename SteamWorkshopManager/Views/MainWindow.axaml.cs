using System;
using Avalonia.Controls;
using Avalonia.Media;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.ViewModels;

namespace SteamWorkshopManager.Views;

public partial class MainWindow : Window
{
    private static readonly IBrush ConnectedBrush = new SolidColorBrush(Color.Parse("#a4d007"));
    private static readonly IBrush DisconnectedBrush = new SolidColorBrush(Color.Parse("#c23b2e"));

    // Required by the XAML previewer.
    public MainWindow() => InitializeComponent();

    public MainWindow(MainViewModel viewModel) : this()
    {
        DataContext = viewModel;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSteamConnected))
            {
                var indicator = this.FindControl<Border>("StatusIndicator");
                if (indicator is not null)
                {
                    indicator.Background = viewModel.IsSteamConnected
                        ? ConnectedBrush
                        : DisconnectedBrush;
                }
            }
        };

        viewModel.OpenAddSessionWizard += OnOpenAddSessionWizard;
        viewModel.ForceCloseRequested += () =>
        {
            _forceClose = true;
            Close();
        };
    }

    private bool _forceClose;

    /// <summary>
    /// Shows the upload guard instead of closing when an upload is running.
    /// Also called from App's ShutdownRequested (e.g. Cmd+Q on macOS).
    /// </summary>
    public bool InterceptCloseDuringUpload()
    {
        if (_forceClose || DataContext is not MainViewModel { IsUploadInProgress: true } vm) return false;
        vm.ShowCloseDuringUploadConfirmation = true;
        return true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (InterceptCloseDuringUpload()) e.Cancel = true;
        base.OnClosing(e);
    }

    private async void OnOpenAddSessionWizard()
    {
        if (DataContext is not MainViewModel vm) return;

        // The dialog auto-closes itself on success; the view-model then routes the
        // new session through the same switch path as a manual pick.
        try
        {
            var addSessionWindow = new AddSessionWindow(vm.CreateAddSessionViewModel());
            addSessionWindow.SessionCreatedAndReady += async () => await vm.OnSessionAddedAsync();
            await addSessionWindow.ShowDialog(this);
        }
        catch (Exception ex)
        {
            LogService.GetLogger<MainWindow>().Error("Add-session dialog failed", ex);
        }
    }
}
