using Avalonia;
using System;
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
        // Ensure STA thread for drag and drop on Windows
        if (OperatingSystem.IsWindows())
        {
            Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
            Thread.CurrentThread.SetApartmentState(ApartmentState.STA);
            OleInitialize(IntPtr.Zero);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}