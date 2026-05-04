using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SteamWorkshopManager.Services.Log;

public class LogService : ILogService
{
    private static LogService? _instance;
    public static LogService Instance => _instance ??= new LogService();

    private readonly List<LogEntry> _logs = [];
    private readonly object _lock = new();
    private readonly string _logFilePath;
    private bool _isDebugEnabled;
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
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamWorkshopManager"
        );
        Directory.CreateDirectory(appDataPath);
        _logFilePath = Path.Combine(appDataPath, $"debug_{DateTime.Now:yyyy-MM-dd}.log");
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
        if (!_isDebugEnabled) return;
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

    public void Info(string source, string message)
    {
        if (_isDebugEnabled)
        {
            Log(LogLevel.Info, source, message);
        }
    }

    public void Warning(string source, string message)
    {
        if (_isDebugEnabled)
        {
            Log(LogLevel.Warning, source, message);
        }
    }

    public void Error(string source, string message, Exception? exception = null)
    {
        // Errors are always logged when debug mode is enabled
        if (_isDebugEnabled)
        {
            Log(LogLevel.Error, source, message, exception);
        }
    }

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

    private void WriteEntryToDisk(LogEntry entry)
    {
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

            lock (_lock)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore file write errors
        }
    }

    public string GetLogFilePath() => _logFilePath;

    public IReadOnlyList<LogEntry> GetRecentLogs(int count = 100)
    {
        lock (_lock)
        {
            return _logs.TakeLast(count).ToList().AsReadOnly();
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
            if (File.Exists(_logFilePath))
            {
                File.Delete(_logFilePath);
            }
        }
        catch
        {
            // Ignore
        }
    }
}
