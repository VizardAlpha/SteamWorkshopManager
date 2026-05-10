using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SteamWorkshopManager.ViewModels;

namespace SteamWorkshopManager.Views;

public partial class AddSessionWindow : Window
{
    public event Action? SessionCreatedAndReady;

    public AddSessionWindow()
    {
        InitializeComponent();

        var viewModel = ActivatorUtilities.CreateInstance<AddSessionViewModel>(App.Services);
        viewModel.SessionCreated += () =>
        {
            SessionCreatedAndReady?.Invoke();
            Close();
        };
        viewModel.CancelRequested += () => Close();
        DataContext = viewModel;
    }
}
