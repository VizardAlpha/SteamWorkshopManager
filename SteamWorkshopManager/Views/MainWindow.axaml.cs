using Avalonia.Controls;
using Avalonia.Media;
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
    }
}
