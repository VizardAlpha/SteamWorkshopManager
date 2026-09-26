using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Notifications;

/// <summary>
/// In-app update: download the release zip, verify it against the release's
/// SHA256SUMS, extract it, then relaunch the extracted copy in installer mode.
/// The installer waits for this process to exit, copies itself over the install
/// folder and starts the new version. No script, same code path on every OS.
/// </summary>
public static class AppUpdater
{
    private static readonly Logger Log = new(nameof(AppUpdater), LogService.Instance);

    public const string ApplyUpdateArg = "--apply-update";
    private const string TargetArg = "--target";
    private const string PidArg = "--pid";

    private const long MaxPackageBytes = 500L * 1024 * 1024;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
        DefaultRequestHeaders = { { "User-Agent", $"SteamWorkshopManager/{AppInfo.Version}" } },
    };

    private static string InstallDir => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary>
    /// Self-update needs the app to run as its own executable (not `dotnet x.dll`)
    /// from a folder the user can write to.
    /// </summary>
    public static bool CanSelfUpdate()
    {
        var exe = Environment.ProcessPath;
        if (exe is null || !string.Equals(Path.GetDirectoryName(exe), InstallDir, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var probe = Path.Combine(InstallDir, $".swm-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Downloads, verifies and extracts the package. Returns the folder holding the new executable.</summary>
    public static async Task<string> DownloadAndStageAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        if (!info.CanInstallInApp || info.PackageName is null)
            throw new InvalidOperationException("Release has no installable package for this platform");
        if (!UpdateCheckerService.IsTrustedAssetUrl(info.PackageUrl) || !UpdateCheckerService.IsTrustedAssetUrl(info.ChecksumsUrl))
            throw new InvalidOperationException("Untrusted update URL");

        var workDir = Path.Combine(AppPaths.Updates, SafeFolderName(info.LatestVersion));
        if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        Directory.CreateDirectory(workDir);

        var sums = await Http.GetStringAsync(info.ChecksumsUrl, ct);
        var expected = ParseChecksum(sums, info.PackageName)
                       ?? throw new InvalidOperationException($"{info.PackageName} is missing from {UpdateCheckerService.ChecksumsAssetName}");

        var zipPath = Path.Combine(workDir, info.PackageName);
        var actual = await DownloadWithHashAsync(info.PackageUrl!, zipPath, info.PackageSize, progress, ct);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Checksum mismatch, the download is corrupted or was tampered with");

        Log.Info($"Update {info.LatestVersion} downloaded and verified");

        var extractDir = Path.Combine(workDir, "extracted");
        // ExtractToDirectory rejects entries that would land outside extractDir.
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        var exeName = Path.GetFileName(Environment.ProcessPath!);
        var payload = FindPayloadRoot(extractDir, exeName)
                      ?? throw new InvalidOperationException($"{exeName} not found in the update package");

        if (!OperatingSystem.IsWindows())
        {
            var exePath = Path.Combine(payload, exeName);
            File.SetUnixFileMode(exePath, File.GetUnixFileMode(exePath)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        return payload;
    }

    /// <summary>Starts the staged copy in installer mode. The caller must then shut the app down.</summary>
    public static void LaunchInstaller(string payloadDir)
    {
        var exe = Path.Combine(payloadDir, Path.GetFileName(Environment.ProcessPath!));
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = payloadDir,
        };
        psi.ArgumentList.Add(ApplyUpdateArg);
        psi.ArgumentList.Add(TargetArg);
        psi.ArgumentList.Add(InstallDir);
        psi.ArgumentList.Add(PidArg);
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(psi);
        Log.Info("Update installer started, shutting down");
    }

    /// <summary>Installer mode entry point. Returns false when the args aren't an install request.</summary>
    public static bool TryRunInstaller(string[] args)
    {
        if (Array.IndexOf(args, ApplyUpdateArg) < 0) return false;

        var target = ArgValue(args, TargetArg);
        var pidText = ArgValue(args, PidArg);
        if (target is null || !Directory.Exists(target))
        {
            Log.Error("Update installer: missing or invalid target folder");
            return true;
        }

        try
        {
            if (int.TryParse(pidText, out var pid)) WaitForExit(pid, TimeSpan.FromSeconds(60));

            var source = InstallDir;
            Log.Info($"Update installer: copying new version into {target}");
            CopyDirectory(source, target);
            Log.Info("Update installer: done");
        }
        catch (Exception ex)
        {
            Log.Error("Update installer failed, relaunching the previous version", ex);
        }

        // Relaunch whatever is in the install folder now (new version, or old one after a failure).
        try
        {
            var exe = Path.Combine(target, Path.GetFileName(Environment.ProcessPath!));
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = target });
        }
        catch (Exception ex)
        {
            Log.Error("Update installer could not relaunch the app", ex);
        }
        return true;
    }

    /// <summary>Removes leftover update packages. Skipped when running from inside them (installer mode).</summary>
    public static void CleanupStaleUpdates()
    {
        try
        {
            if (!Directory.Exists(AppPaths.Updates)) return;
            if (InstallDir.StartsWith(AppPaths.Updates, StringComparison.OrdinalIgnoreCase)) return;
            Directory.Delete(AppPaths.Updates, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not clean update folder: {ex.Message}");
        }
    }

    /// <summary>Reads the hash for <paramref name="fileName"/> from sha256sum output ("hash  name" or "hash *name").</summary>
    public static string? ParseChecksum(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space <= 0) continue;

            var hash = line[..space];
            var name = line[(space + 1)..].TrimStart(' ', '*');
            if (hash.Length == 64 && string.Equals(Path.GetFileName(name), fileName, StringComparison.Ordinal))
                return hash;
        }
        return null;
    }

    /// <summary>Folder holding the executable: the zip root, or its single top folder (older Unix zips had "dist/").</summary>
    public static string? FindPayloadRoot(string extractDir, string exeName)
    {
        if (File.Exists(Path.Combine(extractDir, exeName))) return extractDir;

        foreach (var dir in Directory.GetDirectories(extractDir))
        {
            if (File.Exists(Path.Combine(dir, exeName))) return dir;
        }
        return null;
    }

    private static async Task<string> DownloadWithHashAsync(string url, string path, long expectedSize,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? expectedSize;
        if (total > MaxPackageBytes) throw new InvalidOperationException("Update package is unexpectedly large");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using (var target = File.Create(path))
        {
            var buffer = new byte[81920];
            long read = 0;
            var lastPercent = -1;
            int n;
            while ((n = await source.ReadAsync(buffer, ct)) > 0)
            {
                read += n;
                if (read > MaxPackageBytes) throw new InvalidOperationException("Update package is unexpectedly large");

                hash.AppendData(buffer, 0, n);
                await target.WriteAsync(buffer.AsMemory(0, n), ct);

                if (total > 0)
                {
                    var percent = (int)(read * 100 / total);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(percent);
                    }
                }
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.WaitForExit(timeout))
                Log.Warning("Update installer: previous instance still running, copying anyway");
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            CopyWithRetry(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    // The Steam worker (same exe) can hold files for a moment after the shell exits.
    private static void CopyWithRetry(string from, string to)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(from, to, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 40)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException) when (attempt < 40)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string SafeFolderName(string version)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) version = version.Replace(c, '_');
        return version.Replace("..", "_");
    }
}
