using System;
using Avalonia.Controls;
using SteamWorkshopManager.Services;
using SteamWorkshopManager.ViewModels;

namespace SteamWorkshopManager.Views;

public partial class SetupWizardWindow : Window
{
    public event Action? SessionCreatedAndReady;

    public SetupWizardWindow()
    {
        InitializeComponent();
    }

    public SetupWizardWindow(ISessionRepository sessionRepository) : this()
    {
        var viewModel = new SetupWizardViewModel(sessionRepository);
        viewModel.SessionCreated += OnSessionCreated;
        DataContext = viewModel;
    }

    private void OnSessionCreated()
    {
        // Notify that session is ready (App.axaml.cs will handle showing MainWindow)
        SessionCreatedAndReady?.Invoke();
    }
}
