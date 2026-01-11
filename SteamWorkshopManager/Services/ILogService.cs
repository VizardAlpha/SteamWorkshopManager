using System;
using System.Collections.Generic;

namespace SteamWorkshopManager.Services;

/// <summary>
/// Interface for the logging service.
/// </summary>
public interface ILogService
{
    bool IsDebugEnabled { get; }
    void SetDebugMode(bool enabled);

    void Debug(string source, string message);
    void Info(string source, string message);
    void Warning(string source, string message);
    void Error(string source, string message, Exception? exception = null);

    string GetLogFilePath();
    IReadOnlyList<LogEntry> GetRecentLogs(int count = 100);
    void ClearLogs();
}

/// <summary>
/// Logger instance for a specific class.
/// </summary>
public class Logger(string sourceName, ILogService logService)
{
    public void Debug(string message) => logService.Debug(sourceName, message);
    public void Info(string message) => logService.Info(sourceName, message);
    public void Warning(string message) => logService.Warning(sourceName, message);
    public void Error(string message, Exception? exception = null) => logService.Error(sourceName, message, exception);
}

public record LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string Source,
    string Message,
    string? Exception = null
);

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}
