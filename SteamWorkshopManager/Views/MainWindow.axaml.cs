using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using SteamWorkshopManager.ViewModels;

namespace SteamWorkshopManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainViewModel();
        DataContext = viewModel;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSteamConnected))
            {
                var indicator = this.FindControl<Ellipse>("StatusIndicator");
                if (indicator is not null)
                {
                    indicator.Fill = viewModel.IsSteamConnected
                        ? Brushes.Green
                        : Brushes.Red;
                }
            }
        };
    }
}
