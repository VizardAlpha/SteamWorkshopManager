using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using WorkshopManager.Services.Log;

namespace WorkshopManager.Services.UI;

public sealed class ShellLauncher : IShellLauncher
{
    private static readonly Logger Log = new(nameof(ShellLauncher), LogService.Instance);

    /// <summary>The launcher every caller goes through; tests swap it for a fake.</summary>
    public static IShellLauncher Current { get; set; } = new ShellLauncher();

    public static bool IsWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public bool OpenUrl(string? url)
    {
        if (!IsWebUrl(url))
        {
            Log.Warning($"Refused to open a non-web URL: {url}");
            return false;
        }

        return Start(new ProcessStartInfo(url!) { UseShellExecute = true }, url!);
    }

    public bool OpenFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
        return Start(new ProcessStartInfo(folder) { UseShellExecute = true }, folder);
    }

    public bool RevealFile(string? filePath)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;

        if (OperatingSystem.IsWindows())
            return Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true }, filePath!);
        if (OperatingSystem.IsMacOS())
            return Start(new ProcessStartInfo("open", $"-R \"{filePath}\""), filePath!);

        // Linux file managers share no "select this file" switch.
        return OpenFolder(folder);
    }

    public async Task<bool> CopyTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                || desktop.MainWindow?.Clipboard is not { } clipboard)
                return false;

            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug($"Clipboard copy failed: {ex.Message}");
            return false;
        }
    }

    private static bool Start(ProcessStartInfo info, string target)
    {
        try
        {
            using var process = Process.Start(info);
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to open {target}: {ex.Message}");
            return false;
        }
    }
}
