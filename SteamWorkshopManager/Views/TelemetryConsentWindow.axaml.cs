using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SteamWorkshopManager.Views;

public partial class TelemetryConsentWindow : Window
{
    public TelemetryConsentWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}