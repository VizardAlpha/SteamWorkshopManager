using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Steamworks;
using SteamWorkshopManager.Services;
using SteamWorkshopManager.Views;
using System.Linq;
using System.Threading.Tasks;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager;

public partial class App : Application
{
    private static readonly Logger Log = LogService.GetLogger<App>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Initialize debug mode from settings
            var settingsService = new SettingsService();
            LogService.Instance.SetDebugMode(settingsService.Settings.DebugMode);
            DisableAvaloniaDataAnnotationValidation();

            desktop.ShutdownRequested += OnShutdownRequested;

            // Start async initialization after framework is ready
            Dispatcher.UIThread.Post(() => InitializeAppAsync(desktop, settingsService));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void InitializeAppAsync(IClassicDesktopStyleApplicationLifetime desktop, ISettingsService settingsService)
    {
        try
        {
            var sessionRepository = new SessionRepository(settingsService);

            // Check if we have an active session
            var session = await InitializeSessionAsync(sessionRepository);

            if (session == null)
            {
                // No session - show setup wizard as main window
                Log.Info("No active session, showing setup wizard");
                var wizard = new SetupWizardWindow(sessionRepository);
                var sessionWasCreated = false;

                // When session is created, show MainWindow then close wizard
                wizard.SessionCreatedAndReady += async () =>
                {
                    try
                    {
                        sessionWasCreated = true;

                        var newSession = await sessionRepository.GetActiveSessionAsync();
                        if (newSession == null)
                        {
                            Log.Error("Session was supposed to be created but is null");
                            return;
                        }

                        // Initialize AppConfig and show main window FIRST
                        AppConfig.Initialize(newSession);
                        Log.Info($"Starting with session: {newSession.GameName} (AppId: {newSession.AppId})");

                        var mainWindow = new MainWindow();
                        desktop.MainWindow = mainWindow;
                        mainWindow.Show();

                        // Now close the wizard
                        wizard.Close();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Error creating main window after wizard", ex);
                    }
                };

                // If wizard is closed without creating session, exit
                wizard.Closed += (_, _) =>
                {
                    if (!sessionWasCreated)
                    {
                        Log.Info("Setup wizard closed without creating session, exiting");
                        desktop.Shutdown();
                    }
                };

                desktop.MainWindow = wizard;
                wizard.Show();
            }
            else
            {
                // Initialize AppConfig with the session
                AppConfig.Initialize(session);
                Log.Info($"Starting with session: {session.GameName} (AppId: {session.AppId})");

                desktop.MainWindow = new MainWindow();
                desktop.MainWindow.Show();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to initialize application", ex);
            desktop.Shutdown();
        }
    }

    private static async Task<Models.WorkshopSession?> InitializeSessionAsync(ISessionRepository sessionRepository)
    {
        try
        {
            // Try to get active session
            var session = await sessionRepository.GetActiveSessionAsync();
            if (session != null)
            {
                Log.Debug($"Found active session: {session.Name}");
                return session;
            }

            // Check if we have any sessions at all
            var hasSessions = await sessionRepository.HasSessionsAsync();
            if (hasSessions)
            {
                // We have sessions but no active one - set the first as active
                var sessions = await sessionRepository.GetAllSessionsAsync();
                if (sessions.Count > 0)
                {
                    session = sessions[0];
                    await sessionRepository.SetActiveSessionAsync(session.Id);
                    Log.Info($"Set first available session as active: {session.Name}");
                    return session;
                }
            }

            // Try to migrate from legacy steam_appid.txt
            var sessionManager = new SessionManager(sessionRepository);
            session = await sessionManager.EnsureSessionFromAppIdFileAsync();
            if (session != null)
            {
                Log.Info($"Migrated session from steam_appid.txt: {session.Name}");
                return session;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error initializing session", ex);
        }

        // No session available
        return null;
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Only shutdown if Steam API is still initialized
        // (May have been shut down already by SessionManager during restart)
        try
        {
            if (SteamAPI.IsSteamRunning())
            {
                Log.Debug("Shutting down Steam API on app exit");
                SteamAPI.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Steam shutdown during app exit: {ex.Message}");
        }
    }

#pragma warning disable IL2026
    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
#pragma warning restore IL2026
}
