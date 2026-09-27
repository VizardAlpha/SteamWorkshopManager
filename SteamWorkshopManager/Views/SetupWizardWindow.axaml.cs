using System;
using Avalonia.Controls;
using SteamWorkshopManager.ViewModels;

namespace SteamWorkshopManager.Views;

public partial class SetupWizardWindow : Window
{
    public event Action? SessionCreatedAndReady;

    // Required by the XAML previewer.
    public SetupWizardWindow() => InitializeComponent();

    public SetupWizardWindow(SetupWizardViewModel viewModel) : this()
    {
        // App.axaml.cs shows the MainWindow once the session is ready.
        viewModel.SessionCreated += () => SessionCreatedAndReady?.Invoke();
        DataContext = viewModel;
    }
}
