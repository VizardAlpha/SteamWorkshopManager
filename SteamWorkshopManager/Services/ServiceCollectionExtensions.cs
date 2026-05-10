using Microsoft.Extensions.DependencyInjection;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Core.Steam;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Telemetry;
using SteamWorkshopManager.Services.UI;
using SteamWorkshopManager.Services.Workshop;

namespace SteamWorkshopManager.Services;

/// <summary>
/// Central registration point for the application's services.
/// All services use <c>AddSingleton</c> because this is a single-user desktop app
/// where shared state is expected and instance reuse avoids redundant work.
/// Static classes (<see cref="BundleService"/>, <see cref="SteamAuthService"/>,
/// <see cref="SteamWebClient"/>, <see cref="SteamErrorMapper"/>,
/// <see cref="SteamHttpClientFactory"/>, <see cref="UpdateCheckerService"/>)
/// are not registered here — they are consumed directly by callers.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        // Persistence + settings
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISessionRepository, SessionRepository>();

        // Logging bridges the existing static instance so legacy call sites keep working
        services.AddSingleton<ILogService>(_ => LogService.Instance);

        // Localization bridges the existing static instance the same way
        services.AddSingleton<LocalizationService>(_ => LocalizationService.Instance);

        // Telemetry has a static Initialize pattern; the factory wires it through DI
        services.AddSingleton<ITelemetryService>(sp =>
        {
            TelemetryService.Initialize(sp.GetRequiredService<ISettingsService>());
            return TelemetryService.Instance!;
        });

        // Steam-facing services
        // Shell → worker (out-of-process). The worker process owns SteamAPI state;
        // the shell only speaks to it via JSON-RPC through SessionHost.
        services.AddSingleton<SessionHost>();
        services.AddSingleton<ISteamService, WorkerSteamService>();
        services.AddSingleton<VersioningService>();
        services.AddSingleton<SteamAppMetadataService>();

        // UI-facing services
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();

        // Workshop + dependency services
        services.AddSingleton<WorkshopTagsService>();
        services.AddSingleton<WorkshopDownloadService>();
        services.AddSingleton<ChangelogScraperService>();
        services.AddSingleton<DependencyService>();
        services.AddSingleton<AppDependencyService>();
        services.AddSingleton<AppIdValidator>();
        services.AddSingleton<DraftService>();
        services.AddSingleton<TagSelectionService>();
        services.AddSingleton<WorkshopOrchestrator>();

        // Session orchestration
        services.AddSingleton<SessionManager>();
        services.AddSingleton<SessionCleanupService>();

        return services;
    }
}
