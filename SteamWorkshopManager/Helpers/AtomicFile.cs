using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SteamWorkshopManager.Helpers;

/// <summary>
/// Crash-safe text writes: content goes to a sibling temp file which then replaces
/// the target, so readers never see a truncated file. Writes to the same path are serialized.
/// </summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static void WriteAllText(string path, string contents)
    {
        var gate = GateFor(path);
        gate.Wait();
        try
        {
            var tmp = TempPathFor(path);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8NoBom.GetBytes(contents);
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public static async Task WriteAllTextAsync(string path, string contents)
    {
        var gate = GateFor(path);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var tmp = TempPathFor(path);
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                             bufferSize: 4096, useAsync: true))
            {
                await fs.WriteAsync(Utf8NoBom.GetBytes(contents)).ConfigureAwait(false);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private static SemaphoreSlim GateFor(string path) =>
        Locks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

    private static string TempPathFor(string path) => path + ".tmp";
}
