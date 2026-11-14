using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using WorkshopManager.Helpers;
using WorkshopManager.Models;
using WorkshopManager.Services.Log;

namespace WorkshopManager.Services.Notifications;

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
        DefaultRequestHeaders = { { "User-Agent", $"WorkshopManager/{AppInfo.Version}" } },
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
            InstallPayload(source, target);
            Log.Info("Update installer: done");
        }
        catch (Exception ex)
        {
            Log.Error("Update installer failed, files restored, relaunching the previous version", ex);
        }

        // Relaunch what is in the install folder now: the new version, or the restored one after a failure.
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

    internal const string ManifestName = "update-manifest.txt";
    private const string BackupFolderName = ".update-backup";

    /// <summary>
    /// Copies the payload over the install folder, all or nothing: replaced files are set
    /// aside first and put back if any step fails. Files a previous update installed and
    /// that this payload no longer ships are removed, per <see cref="ManifestName"/>.
    /// </summary>
    internal static void InstallPayload(string source, string target, int attempts = 40)
    {
        var backup = Path.Combine(target, BackupFolderName);
        if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);

        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(source, file))
            .Where(rel => !rel.StartsWith(BackupFolderName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var replaced = new List<string>();
        var added = new List<string>();

        try
        {
            foreach (var rel in files)
            {
                var to = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);

                if (File.Exists(to))
                {
                    var kept = Path.Combine(backup, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
                    Retry(() => File.Move(to, kept), attempts);
                    replaced.Add(rel);
                }
                else
                {
                    added.Add(rel);
                }

                Retry(() => File.Copy(Path.Combine(source, rel), to, overwrite: true), attempts);
            }
        }
        catch
        {
            Rollback(target, backup, replaced, added);
            throw;
        }

        RemoveFilesNoLongerShipped(target, files);
        try { File.WriteAllLines(Path.Combine(target, ManifestName), files); }
        catch (Exception ex) { Log.Warning($"Update manifest not written: {ex.Message}"); }

        try { Directory.Delete(backup, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Removed at the start of the next update.
        }
    }

    private static void Rollback(string target, string backup, List<string> replaced, List<string> added)
    {
        foreach (var rel in added)
        {
            try
            {
                var path = Path.Combine(target, rel);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) { Log.Warning($"Rollback could not remove {rel}: {ex.Message}"); }
        }

        foreach (var rel in replaced)
        {
            try { File.Move(Path.Combine(backup, rel), Path.Combine(target, rel), overwrite: true); }
            catch (Exception ex) { Log.Error($"Rollback could not restore {rel}: {ex.Message}"); }
        }
    }

    // Only what an earlier update listed: files the user or the app added by themselves stay.
    private static void RemoveFilesNoLongerShipped(string target, List<string> shipped)
    {
        var manifest = Path.Combine(target, ManifestName);
        if (!File.Exists(manifest)) return;

        try
        {
            var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
            var kept = shipped.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in File.ReadAllLines(manifest))
            {
                if (string.IsNullOrWhiteSpace(rel) || kept.Contains(rel)) continue;

                var path = Path.GetFullPath(Path.Combine(target, rel));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Old files not cleaned up: {ex.Message}");
        }
    }

    // The Steam worker (same exe) can hold files for a moment after the shell exits.
    private static void Retry(Action action, int attempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < attempts)
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
