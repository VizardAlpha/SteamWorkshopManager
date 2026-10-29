using System;
using System.IO;
using System.Security.Cryptography;

namespace SteamWorkshopManager.Helpers;

/// <summary>
/// Secrets on disk: DPAPI (current user) on Windows, owner-only file permissions elsewhere.
/// Each caller passes its own entropy so one store can't read another's blob.
/// </summary>
public static class ProtectedFile
{
    public static byte[] Read(string path, byte[] entropy) => Unprotect(File.ReadAllBytes(path), entropy);

    /// <summary>Written to a temp file then renamed, so a crash never leaves half a secret.</summary>
    public static void Write(string path, byte[] data, byte[] entropy)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, CreateOptions()))
        {
            fs.Write(Protect(data, entropy));
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static FileStreamOptions CreateOptions()
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }

    private static byte[] Protect(byte[] data, byte[] entropy) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser)
            : data;

    private static byte[] Unprotect(byte[] data, byte[] entropy) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(data, entropy, DataProtectionScope.CurrentUser)
            : data;
}
