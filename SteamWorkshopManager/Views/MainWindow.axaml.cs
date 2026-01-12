using Avalonia.Controls;
using Avalonia.Media;
using SteamWorkshopManager.Services;
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
    }

    private async void OnOpenAddSessionWizard()
    {
        var settingsService = new SettingsService();
        var sessionRepository = new SessionRepository(settingsService);
        var addSessionWindow = new AddSessionWindow(sessionRepository);

        // When session is created, switch to it (which restarts the app)
        addSessionWindow.SessionCreatedAndReady += async () =>
        {
            var session = await sessionRepository.GetActiveSessionAsync();
            if (session != null)
            {
                var sessionManager = new SessionManager(sessionRepository);
                await sessionManager.SwitchSessionAsync(session);
            }
        };

        await addSessionWindow.ShowDialog(this);
    }
}
