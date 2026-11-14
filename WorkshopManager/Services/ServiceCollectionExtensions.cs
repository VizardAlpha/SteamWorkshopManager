using Microsoft.Extensions.DependencyInjection;
using WorkshopManager.Core.Sessions;
using WorkshopManager.Core.Steam;
using WorkshopManager.Core.Workshop;
using WorkshopManager.Services.Cache;
using WorkshopManager.Services.Core;
using WorkshopManager.Services.Log;
using WorkshopManager.Services.Notifications;
using WorkshopManager.Services.Platforms;
using WorkshopManager.Services.Presence;
using WorkshopManager.Services.Session;
using WorkshopManager.Services.Steam;
using WorkshopManager.Services.Telemetry;
using WorkshopManager.Services.UI;
using WorkshopManager.Services.Workshop;

namespace WorkshopManager.Services;

/// <summary>
/// Central registration point for the application's services.
/// Services are singletons (single-user desktop app, shared state is expected);
/// view-models built per view are transient.
/// Static classes (<see cref="BundleService"/>, <see cref="SteamAuthService"/>,
/// <see cref="SteamWebClient"/>, <see cref="SteamErrorMapper"/>,
/// <see cref="AppHttpClientFactory"/>, <see cref="UpdateCheckerService"/>)
/// are not registered here - they are consumed directly by callers.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        // Active session, read wherever the current game matters
        services.AddSingleton<ISessionContext, SessionContext>();

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

        // Platforms behind the shared screens; the active session picks one.
        services.AddSingleton<IModPlatform, SteamModPlatform>();
        services.AddSingleton<IModPlatform, Platforms.Modio.ModioModPlatform>();
        services.AddSingleton<ModPlatforms>();
        services.AddSingleton<Platforms.Modio.ModioApiClient>();
        services.AddSingleton<Platforms.Modio.ModioAccount>();
        services.AddSingleton<Platforms.Modio.ModioPublisher>();
        services.AddSingleton<SteamAppMetadataService>();

        // UI-facing services
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<UploadCancellation>();
        services.AddSingleton<IShellLauncher>(_ => ShellLauncher.Current);
        services.AddSingleton<IDiscordPresenceService, DiscordPresenceService>();
        services.AddSingleton<ViewModels.UpdateController>();

        // Cache shared by every platform
        services.AddSingleton<ICacheStore, CacheStore>();
        services.AddSingleton<SteamImageCache>();
        services.AddSingleton<ThumbnailCache>();

        // Workshop + dependency services
        services.AddSingleton<WorkshopTagsService>();
        services.AddSingleton<PublishedItemsCache>();
        services.AddSingleton<WorkshopDownloadService>();
        services.AddSingleton<ChangelogScraperService>();
        services.AddSingleton<DependencyService>();
        services.AddSingleton<AppDependencyService>();
        services.AddSingleton<ItemTranslationService>();
        services.AddSingleton<AppIdValidator>();
        services.AddSingleton<DraftService>();
        services.AddSingleton<TagSelectionService>();
        services.AddSingleton<WorkshopOrchestrator>();

        // Session orchestration
        services.AddSingleton<SessionManager>();
        services.AddSingleton<SessionCleanupService>();

        // View-models: the shell is built once, the rest through the factory (they take runtime args)
        services.AddSingleton<ViewModels.IViewModelFactory, ViewModels.ViewModelFactory>();
        services.AddTransient<ViewModels.MainViewModel>();

        // Editor sections shared by the create and edit views: one instance per view.
        services.AddTransient<ViewModels.Editor.TagEditorViewModel>();
        services.AddTransient<ViewModels.Editor.VersionRangeViewModel>();
        services.AddTransient<ViewModels.Editor.DependencyEditorViewModel>();
        services.AddTransient<ViewModels.Editor.PreviewGalleryViewModel>();
        services.AddTransient<ViewModels.Editor.ChangelogHistoryViewModel>();
        services.AddTransient<ViewModels.Editor.TranslationEditorViewModel>();
        services.AddTransient<ViewModels.ModioSignInViewModel>();
        services.AddTransient<ViewModels.ModioSessionSetupViewModel>();
        services.AddTransient<ViewModels.Modio.ModioDetailsViewModel>();
        services.AddTransient<ViewModels.Modio.ModioMediaViewModel>();
        services.AddTransient<ViewModels.Modio.ModioFileViewModel>();
        services.AddTransient<ViewModels.Modio.ModioDependenciesViewModel>();

        return services;
    }
}
