using System;
using Avalonia.Controls;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.ViewModels;

namespace SteamWorkshopManager.Views;

public partial class AddSessionWindow : Window
{
    public event Action? SessionCreatedAndReady;

    public AddSessionWindow()
    {
        InitializeComponent();
    }

    public AddSessionWindow(ISessionRepository sessionRepository) : this()
    {
        var viewModel = new AddSessionViewModel(sessionRepository);
        viewModel.SessionCreated += () => SessionCreatedAndReady?.Invoke();
        viewModel.CancelRequested += () => Close();
        DataContext = viewModel;
    }
}
