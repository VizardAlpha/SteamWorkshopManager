using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using SteamWorkshopManager.Services;
using SteamWorkshopManager.Services.Steam.Worker.Contracts;
using SteamWorkshopManager.Services.Steam.Worker.Host;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace SteamWorkshopManager;

sealed class Program
{
    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Worker mode: the same binary bifurcates on --steam-worker, skipping Avalonia entirely.
        // Session switching kills and respawns this process without touching the shell.
        if (SteamWorkerArgs.TryParse(args, out var workerArgs))
        {
            SteamWorkerHost.RunAsync(workerArgs).GetAwaiter().GetResult();
            return;
        }

        // Dev affordance: `--force-setup-wizard` makes startup treat the session
        // repository as empty so the wizard is shown even when a session exists.
        // Useful for iterating on the wizard UI without wiping the sessions file.
        App.ForceSetupWizard = Array.Exists(args, a =>
            string.Equals(a, "--force-setup-wizard", StringComparison.OrdinalIgnoreCase));

        // Ensure STA thread for drag and drop on Windows
        if (OperatingSystem.IsWindows())
        {
            Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
            Thread.CurrentThread.SetApartmentState(ApartmentState.STA);
            OleInitialize(IntPtr.Zero);
        }

        App.Services = new ServiceCollection()
            .AddAppServices()
            .BuildServiceProvider();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
