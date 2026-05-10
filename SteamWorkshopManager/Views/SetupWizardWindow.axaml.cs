using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SteamWorkshopManager.ViewModels;

namespace SteamWorkshopManager.Views;

public partial class SetupWizardWindow : Window
{
    public event Action? SessionCreatedAndReady;

    public SetupWizardWindow()
    {
        InitializeComponent();

        var viewModel = ActivatorUtilities.CreateInstance<SetupWizardViewModel>(App.Services);
        viewModel.SessionCreated += OnSessionCreated;
        DataContext = viewModel;
    }

    private void OnSessionCreated()
    {
        // Notify that session is ready (App.axaml.cs will handle showing MainWindow)
        SessionCreatedAndReady?.Invoke();
    }
}
