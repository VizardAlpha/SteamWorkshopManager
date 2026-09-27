using System;
using Avalonia.Controls;
using SteamWorkshopManager.ViewModels;

namespace SteamWorkshopManager.Views;

public partial class AddSessionWindow : Window
{
    public event Action? SessionCreatedAndReady;

    // Required by the XAML previewer.
    public AddSessionWindow() => InitializeComponent();

    public AddSessionWindow(AddSessionViewModel viewModel) : this()
    {
        viewModel.SessionCreated += () =>
        {
            SessionCreatedAndReady?.Invoke();
            Close();
        };
        viewModel.CancelRequested += () => Close();
        DataContext = viewModel;
    }
}
