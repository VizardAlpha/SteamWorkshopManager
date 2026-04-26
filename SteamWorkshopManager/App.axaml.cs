using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Views;
using System.Threading.Tasks;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Telemetry;

namespace SteamWorkshopManager;

public partial class App : Application
{
    private static readonly Logger Log = LogService.GetLogger<App>();

    /// <summary>
    /// Root DI container for the application. Built in <see cref="Program.Main"/> before the
    /// Avalonia runtime starts so that any service can be resolved from anywhere in the app.
    /// </summary>
    public static IServiceProvider Services { get; set; } = null!;

    /// <summary>
    /// Dev flag set by <c>--force-setup-wizard</c> on the CLI. Startup then
    /// bypasses the "existing session" branch and always renders the wizard,
    /// so UI work on the wizard can iterate without wiping disk state.
    /// </summary>
    public static bool ForceSetupWizard { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsService = Services.GetRequiredService<ISettingsService>();
            LogService.Instance.SetDebugMode(settingsService.Settings.DebugMode);

            // Force eager instantiation so its static Initialize factory runs now
            Services.GetRequiredService<ITelemetryService>();

            desktop.ShutdownRequested += OnShutdownRequested;

            // Start async initialization after framework is ready
            Dispatcher.UIThread.Post(() => InitializeAppAsync(desktop));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void InitializeAppAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var sessionRepository = Services.GetRequiredService<ISessionRepository>();
            var telemetry = Services.GetRequiredService<ITelemetryService>();

            // Check if we have an active session. The --force-setup-wizard flag
            // pretends there's none so we always land on the wizard UI.
            var session = ForceSetupWizard
                ? null
                : await InitializeSessionAsync(sessionRepository);

            // Track AppStart is deferred: fired only after the wizard closes
            // (or immediately in the existing-session branch) so first-run
            // users never see anything leave the process before they opt in.

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

                        // Spawn the Steam worker for the newly-created session.
                        await Services.GetRequiredService<SessionHost>().StartSessionAsync(newSession.AppId);

                        // The wizard committed the user's telemetry choice to
                        // settings. AppStart fires now (post-consent) and
                        // respects the toggle.
                        telemetry.Track(TelemetryEventTypes.AppStart, newSession.AppId);

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

                // Spawn the Steam worker before the UI queries Steam so the
                // first ISteamService.Initialize() call returns the live state.
                await Services.GetRequiredService<SessionHost>().StartSessionAsync(session.AppId);

                // Existing session = consent already given a previous run. Track.
                telemetry.Track(TelemetryEventTypes.AppStart, session.AppId);

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
            var sessionManager = Services.GetRequiredService<SessionManager>();
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
        try
        {
            TelemetryService.ShutdownAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Debug($"Telemetry shutdown: {ex.Message}");
        }
        
        try
        {
            Services.GetService<SessionHost>()?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Debug($"SessionHost shutdown: {ex.Message}");
        }
    }

}
