using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SteamWorkshopManager.Helpers;

namespace SteamWorkshopManager.Services.Log;

public class LogService : ILogService
{
    private static LogService? _instance;
    public static LogService Instance => _instance ??= new LogService();

    private readonly List<LogEntry> _logs = [];
    private readonly Lock _lock = new();
    private readonly string _appLogPath;
    private readonly string _debugLogPath;
    private bool _isDebugEnabled;
    private bool _fileOutputEnabled = true;
    private readonly List<string> _sensitiveValues = [];

    // Worker-side log forwarding: when enabled, file writes are skipped and
    // entries go through _remoteSink (or buffer until it's attached).
    private bool _useRemoteForwarding;
    private Action<LogEntry>? _remoteSink;
    private readonly Queue<LogEntry> _preSinkBuffer = new();
    private const int MaxBufferedPreSink = 500;

    public bool IsDebugEnabled => _isDebugEnabled;

    private readonly string _userProfilePath;

    private LogService()
    {
        Directory.CreateDirectory(AppPaths.LocalRoot);
        var day = DateTime.Now.ToString("yyyy-MM-dd");
        _appLogPath = Path.Combine(AppPaths.LocalRoot, $"app_{day}.log");
        _debugLogPath = Path.Combine(AppPaths.LocalRoot, $"debug_{day}.log");
        _userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>Switches this process into worker mode: writes are forwarded
    /// to the shell via <see cref="SetRemoteSink"/> instead of hitting disk.</summary>
    public void EnableRemoteForwarding()
    {
        lock (_lock) _useRemoteForwarding = true;
    }

    /// <summary>Attaches/detaches the shell-side sink. Flushes any entries
    /// produced before the sink was available.</summary>
    public void SetRemoteSink(Action<LogEntry>? sink)
    {
        List<LogEntry>? toFlush = null;
        lock (_lock)
        {
            _remoteSink = sink;
            if (sink != null && _preSinkBuffer.Count > 0)
            {
                toFlush = new List<LogEntry>(_preSinkBuffer);
                _preSinkBuffer.Clear();
            }
        }
        if (toFlush == null || sink == null) return;
        foreach (var entry in toFlush)
        {
            try { sink(entry); } catch { /* swallow sink failures */ }
        }
    }

    /// <summary>Writes a forwarded entry from another process to this
    /// LogService's file + memory ring. Bypasses remote-sink forwarding.</summary>
    public void IngestRemote(LogLevel level, string source, string message, string? exception, DateTime timestampUtc)
    {
        if (level == LogLevel.Debug && !_isDebugEnabled) return;
        var entry = new LogEntry(timestampUtc.ToLocalTime(), level, source, message, exception);
        lock (_lock)
        {
            _logs.Add(entry);
            if (_logs.Count > 1000) _logs.RemoveAt(0);
        }
        WriteEntryToDisk(entry);
    }

    /// <summary>
    /// Gets a logger instance for the specified class.
    /// </summary>
    /// <example>
    /// private static readonly Logger _log = LogService.GetLogger{MyClass}();
    /// </example>
    public static Logger GetLogger<T>() => new(typeof(T).Name, Instance);

    /// <summary>
    /// Registers a value that should be redacted from all log output (e.g. account name, SteamID64).
    /// </summary>
    public void RegisterSensitiveValue(string value, string replacement)
    {
        if (string.IsNullOrEmpty(value)) return;
        lock (_lock)
        {
            _sensitiveValues.Add(value);
            _sensitiveValues.Add(replacement);
        }
    }

    private string SanitizeMessage(string message)
    {
        // Replace user profile path with %USERPROFILE%
        if (!string.IsNullOrEmpty(_userProfilePath))
            message = message.Replace(_userProfilePath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

        // Redact registered sensitive values
        lock (_lock)
        {
            for (var i = 0; i < _sensitiveValues.Count; i += 2)
            {
                message = message.Replace(_sensitiveValues[i], _sensitiveValues[i + 1]);
            }
        }

        return message;
    }

    /// <summary>
    /// Keeps log entries in memory only. The test suite calls this so a run
    /// doesn't append to the logs of whoever is running it.
    /// </summary>
    public void DisableFileOutput()
    {
        lock (_lock) _fileOutputEnabled = false;
    }

    public void SetDebugMode(bool enabled)
    {
        _isDebugEnabled = enabled;
        if (enabled)
        {
            Info("LogService", "Debug mode enabled");
        }
    }

    public void Debug(string source, string message)
    {
        if (_isDebugEnabled)
        {
            Log(LogLevel.Debug, source, message);
        }
    }

    // Info and above are always recorded. Debug mode only adds the Debug level,
    // so a user hitting a bug still has something to send without having turned
    // anything on beforehand.
    public void Info(string source, string message) => Log(LogLevel.Info, source, message);

    public void Warning(string source, string message) => Log(LogLevel.Warning, source, message);

    public void Error(string source, string message, Exception? exception = null) =>
        Log(LogLevel.Error, source, message, exception);

    private void Log(LogLevel level, string source, string message, Exception? exception = null)
    {
        var entry = new LogEntry(
            DateTime.Now,
            level,
            source,
            SanitizeMessage(message),
            exception != null ? SanitizeMessage(exception.ToString()) : null
        );

        lock (_lock)
        {
            _logs.Add(entry);

            // Keep only last 1000 entries in memory
            if (_logs.Count > 1000)
            {
                _logs.RemoveAt(0);
            }
        }

        WriteToFile(entry);
    }

    private void WriteToFile(LogEntry entry)
    {
        Action<LogEntry>? sink;
        bool buffer;
        lock (_lock)
        {
            sink = _remoteSink;
            buffer = _useRemoteForwarding && sink == null;
            if (buffer)
            {
                _preSinkBuffer.Enqueue(entry);
                while (_preSinkBuffer.Count > MaxBufferedPreSink)
                    _preSinkBuffer.Dequeue();
            }
        }

        if (sink != null)
        {
            try { sink(entry); } catch { /* swallow sink failures */ }
            return;
        }
        if (buffer) return;

        WriteEntryToDisk(entry);
    }

    /// <summary>
    /// Appends, retrying briefly on sharing violations. The shell and the worker
    /// are separate processes, so an in-process lock alone can still lose a line.
    /// </summary>
    internal static void AppendWithRetry(string path, string content)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.AppendAllText(path, content);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(15);
            }
        }
    }

    private void WriteEntryToDisk(LogEntry entry)
    {
        if (!_fileOutputEnabled) return;

        try
        {
            var levelStr = entry.Level switch
            {
                LogLevel.Debug => "DEBUG",
                LogLevel.Info => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                _ => "?????"
            };
            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] [{entry.Source}] {entry.Message}";
            if (entry.Exception != null)
                line += Environment.NewLine + entry.Exception;

            // Debug chatter goes to its own file so it can't drown the entries
            // that matter when diagnosing a user report.
            var path = entry.Level == LogLevel.Debug ? _debugLogPath : _appLogPath;
            lock (_lock)
            {
                AppendWithRetry(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore file write errors
        }
    }

    /// <summary>
    /// File Settings points at. In debug mode that is the debug log, since it is
    /// the one the user just asked to produce; otherwise the regular app log.
    /// </summary>
    public string GetLogFilePath() => _isDebugEnabled ? _debugLogPath : _appLogPath;

    /// <summary>Every file the log folder owns: app, debug and crash, all days.</summary>
    private static IEnumerable<string> EnumerateLogFiles() =>
        Directory.EnumerateFiles(AppPaths.LocalRoot, "app_*.log")
            .Concat(Directory.EnumerateFiles(AppPaths.LocalRoot, "debug_*.log"))
            .Concat(Directory.EnumerateFiles(AppPaths.LocalRoot, "crash_*.log"));

    public IReadOnlyList<LogEntry> GetRecentLogs(int count = 100)
    {
        lock (_lock)
        {
            return _logs.TakeLast(count).ToList().AsReadOnly();
        }
    }

    public long GetLogFolderSize()
    {
        try
        {
            if (!Directory.Exists(AppPaths.LocalRoot)) return 0;
            long total = 0;
            foreach (var path in EnumerateLogFiles())
            {
                try { total += new FileInfo(path).Length; }
                catch { /* race with delete */ }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    public void ClearLogs()
    {
        lock (_lock)
        {
            _logs.Clear();
        }

        try
        {
            if (!Directory.Exists(AppPaths.LocalRoot)) return;
            foreach (var path in EnumerateLogFiles())
            {
                try { File.Delete(path); }
                catch { /* in-use by another process / handle still open */ }
            }
        }
        catch
        {
            // Ignore enumeration failures
        }
    }
}
