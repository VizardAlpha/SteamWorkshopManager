using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SteamWorkshopManager.Services;

public class LogService : ILogService
{
    private static LogService? _instance;
    public static LogService Instance => _instance ??= new LogService();

    private readonly List<LogEntry> _logs = [];
    private readonly object _lock = new();
    private readonly string _logFilePath;
    private bool _isDebugEnabled;

    public bool IsDebugEnabled => _isDebugEnabled;

    private LogService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamWorkshopManager"
        );
        Directory.CreateDirectory(appDataPath);
        _logFilePath = Path.Combine(appDataPath, $"debug_{DateTime.Now:yyyy-MM-dd}.log");
    }

    /// <summary>
    /// Gets a logger instance for the specified class.
    /// </summary>
    /// <example>
    /// private static readonly Logger _log = LogService.GetLogger{MyClass}();
    /// </example>
    public static Logger GetLogger<T>() => new(typeof(T).Name, Instance);

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
            message,
            exception?.ToString()
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

            // Format: [timestamp] [LEVEL] [SourceClass] Message
            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] [{entry.Source}] {entry.Message}";
            if (entry.Exception != null)
            {
                line += Environment.NewLine + entry.Exception;
            }

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
