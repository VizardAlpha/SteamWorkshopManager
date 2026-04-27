using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.Services.Telemetry;

[JsonSerializable(typeof(TelemetryState))]
[JsonSerializable(typeof(TelemetryPayload))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal partial class TelemetryJsonContext : JsonSerializerContext;

public class TelemetryService : ITelemetryService, IDisposable
{
    private static readonly Logger Log = LogService.GetLogger<TelemetryService>();

    public static ITelemetryService? Instance { get; private set; }

    public static void Initialize(ISettingsService settingsService)
    {
        Instance ??= new TelemetryService(settingsService);
    }

    public static async Task ShutdownAsync()
    {
        if (Instance is not TelemetryService svc) return;
        try { await svc.FlushAsync(); } catch { }
        svc.Dispose();
        Instance = null;
    }

    private const int MaxQueueSize = 500;
    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);
    
    private static string ResolveEndpoint()
    {
        // Priority: explicit env var (manual override) > Debug local > baked Release URL.
        // The env var still wins in Debug, so a dev can opt to hit prod from
        // a debug build by exporting SWM_TELEMETRY_URL.
        var runtime = Environment.GetEnvironmentVariable("SWM_TELEMETRY_URL");
        if (!string.IsNullOrWhiteSpace(runtime)) return runtime.Trim();

#if DEBUG
        return "http://localhost:5000/ingest";
#else
        var baked = typeof(TelemetryService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "TelemetryEndpoint")?.Value;
        return string.IsNullOrWhiteSpace(baked) ? string.Empty : baked.Trim();
#endif
    }

    private static readonly string StateFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamWorkshopManager"
    );

    private static readonly string StatePath = Path.Combine(StateFolder, "telemetry.json");

    private static readonly HttpClient Http = SteamHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(10));

    private readonly ISettingsService _settingsService;
    private readonly string _endpoint;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _flushLoop;

    private TelemetryState _state;

    public TelemetryService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _endpoint = ResolveEndpoint();
        _state = LoadOrCreateState();
        Log.Info($"Telemetry initialized: endpoint={(string.IsNullOrEmpty(_endpoint) ? "(none)" : "<redacted>")}, enabled={settingsService.Settings.TelemetryEnabled}, instanceId={_state.InstanceId}, queueCount={_state.Queue.Count}");
        _flushLoop = Task.Run(FlushLoopAsync);
    }

    public Guid InstanceId => _state.InstanceId;

    public void Track(string eventType, uint? steamAppId = null)
    {
        if (!_settingsService.Settings.TelemetryEnabled) return;

        try
        {
            _stateLock.Wait();
            try
            {
                _state.Queue.Add(new TelemetryQueuedEvent
                {
                    Type = eventType,
                    SteamAppId = steamAppId,
                    Timestamp = DateTime.UtcNow,
                });

                if (_state.Queue.Count > MaxQueueSize)
                {
                    _state.Queue.RemoveRange(0, _state.Queue.Count - MaxQueueSize);
                }

                SaveStateUnsafe();
            }
            finally
            {
                _stateLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Telemetry track failed: {ex.Message}");
        }
    }

    public async Task FlushAsync()
    {
        if (!_settingsService.Settings.TelemetryEnabled)
        {
            await ClearQueueAsync();
            return;
        }
        
        if (string.IsNullOrEmpty(_endpoint))
        {
            await ClearQueueAsync();
            return;
        }

        List<TelemetryQueuedEvent> batch;
        await _stateLock.WaitAsync();
        try
        {
            if (_state.Queue.Count == 0) return;
            batch = _state.Queue.Take(BatchSize).ToList();
        }
        finally
        {
            _stateLock.Release();
        }

        var payload = BuildPayload(batch);

        try
        {
            var response = await Http.PostAsJsonAsync(_endpoint, payload, TelemetryJsonContext.Default.TelemetryPayload, _cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                Log.Debug($"Telemetry POST returned {(int)response.StatusCode}");
                return;
            }
            Log.Debug($"Telemetry POST sent {batch.Count} events");
        }
        catch (Exception ex)
        {
            var socketCode = (ex as SocketException ?? ex.InnerException as SocketException)?.SocketErrorCode;
            var detail = socketCode is { } code ? $" ({code})" : "";
            Log.Debug($"Telemetry POST failed: {ex.GetType().Name}{detail}");
            return;
        }

        await _stateLock.WaitAsync();
        try
        {
            _state.Queue.RemoveRange(0, Math.Min(batch.Count, _state.Queue.Count));
            SaveStateUnsafe();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task FlushLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(FlushInterval, _cts.Token);
                    await FlushAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Debug($"Telemetry flush loop error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ClearQueueAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_state.Queue.Count == 0) return;
            _state.Queue.Clear();
            SaveStateUnsafe();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private TelemetryPayload BuildPayload(List<TelemetryQueuedEvent> events)
    {
        return new TelemetryPayload
        {
            InstanceId = _state.InstanceId,
            Os = DetectOs(),
            OsVersion = RuntimeInformation.OSDescription,
            AppVersion = AppInfo.Version,
            Language = _settingsService.Settings.Language,
            Events = events.Select(e => new TelemetryPayloadEvent
            {
                Type = e.Type,
                SteamAppId = e.SteamAppId,
                Timestamp = e.Timestamp,
            }).ToList(),
        };
    }

    private static string DetectOs()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsLinux()) return "Linux";
        return "Unknown";
    }

    private TelemetryState LoadOrCreateState()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                var json = File.ReadAllText(StatePath);
                var loaded = JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.TelemetryState);
                if (loaded is not null && loaded.InstanceId != Guid.Empty)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Telemetry state load failed, creating fresh: {ex.Message}");
        }

        var fresh = new TelemetryState { InstanceId = Guid.NewGuid() };
        try
        {
            Directory.CreateDirectory(StateFolder);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(fresh, TelemetryJsonContext.Default.TelemetryState));
        }
        catch (Exception ex)
        {
            Log.Debug($"Telemetry state save failed: {ex.Message}");
        }
        return fresh;
    }

    private void SaveStateUnsafe()
    {
        try
        {
            Directory.CreateDirectory(StateFolder);
            var json = JsonSerializer.Serialize(_state, TelemetryJsonContext.Default.TelemetryState);
            File.WriteAllText(StatePath, json);
        }
        catch (Exception ex)
        {
            Log.Debug($"Telemetry state save failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            _flushLoop.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
        _cts.Dispose();
        _stateLock.Dispose();
    }
}
