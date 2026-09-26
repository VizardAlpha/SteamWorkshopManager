using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.ViewModels;

namespace SteamWorkshopManager.Views;

public partial class MainWindow : Window
{
    private static readonly IBrush ConnectedBrush = new SolidColorBrush(Color.Parse("#a4d007"));
    private static readonly IBrush DisconnectedBrush = new SolidColorBrush(Color.Parse("#c23b2e"));

    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainViewModel();
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
        var sessionRepository = App.Services.GetRequiredService<ISessionRepository>();
        var addSessionWindow = new AddSessionWindow();

        // The dialog auto-closes itself on success; here we refresh the pill
        // collection and route through the ViewModel's SwitchSessionCommand so
        // the new session flows through the same UI-update path as a manual
        // switch (items reload, hero image refresh, KPI recalc…).
        addSessionWindow.SessionCreatedAndReady += async () =>
        {
            if (DataContext is not MainViewModel vm) return;

            var newActive = await sessionRepository.GetActiveSessionAsync();
            if (newActive is null) return;

            await vm.LoadSessionsAsync();

            var sessionInPill = vm.Sessions.FirstOrDefault(s => s.Id == newActive.Id);
            if (sessionInPill is not null)
            {
                await vm.SwitchSessionCommand.ExecuteAsync(sessionInPill);
            }
        };

        await addSessionWindow.ShowDialog(this);
    }
}
