using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiscordRPC;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Presence;

/// <summary>
/// Publishes what the user is doing to Discord. The connection only exists
/// while <see cref="AppSettings.DiscordPresenceMode"/> is anything but
/// <see cref="DiscordPresenceMode.Off"/>, and the strings are deliberately
/// English: they are read by the user's Discord contacts, not by the user.
///
/// The application id is baked at build time from the <c>DiscordAppId</c>
/// MSBuild property and can be overridden with <c>SWM_DISCORD_APP_ID</c>.
/// Without an id the service stays dormant.
///
/// The <c>app_logo</c> art asset must be uploaded in the Discord Developer
/// Portal (Rich Presence > Art Assets) for the icon to show up.
/// </summary>
public sealed class DiscordPresenceService : IDiscordPresenceService, IDisposable
{
    private const string LogoAssetKey = "app_logo";
    private const string AppName = "Steam Workshop Manager";

    // Discord rejects activity fields longer than 128 bytes.
    private const int MaxFieldLength = 128;

    // Opening an editor changes the view and then the tab, and Discord throttles
    // activity changes (~5 per 20s). Without coalescing, the intermediate state
    // wins the race and the final one is dropped.
    private const int CoalesceDelayMs = 700;

    // The library retries the pipe forever with a growing backoff. Left alone
    // it would log a failure roughly every minute for the whole session on a
    // machine without Discord, so we stop and wait for the user instead.
    private const int MaxConnectionAttempts = 3;

    private static readonly Logger Log = LogService.GetLogger<DiscordPresenceService>();

    private readonly ISettingsService _settings;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly object _gate = new();
    private readonly Timer _coalesce;

    private DiscordRpcClient? _client;
    private PresenceState _state = PresenceState.Idle;
    private DiscordConnectionState _connection = DiscordConnectionState.Disabled;
    private int _failures;

    public DiscordPresenceService(ISettingsService settings)
    {
        _settings = settings;
        _coalesce = new Timer(_ => PublishLocked(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public DiscordConnectionState ConnectionState
    {
        get { lock (_gate) return _connection; }
    }

    public event Action? ConnectionStateChanged;

    private DiscordPresenceMode Mode => _settings.Settings.DiscordPresenceMode;

    public void Sync()
    {
        bool changed;

        lock (_gate)
        {
            if (Mode == DiscordPresenceMode.Off)
            {
                Disconnect();
                changed = SetStateLocked(DiscordConnectionState.Disabled);
            }
            else
            {
                // Reaching here is always a deliberate act (startup, a settings
                // change, the retry button), so it clears a previous give-up.
                _failures = 0;
                changed = false;

                if (_client is null)
                    changed = SetStateLocked(Connect()
                        ? DiscordConnectionState.Connecting
                        : DiscordConnectionState.Unreachable);

                Publish();
            }
        }

        if (changed) ConnectionStateChanged?.Invoke();
    }

    public void Update(PresenceState state)
    {
        lock (_gate)
        {
            _state = state;
            if (Mode == DiscordPresenceMode.Off || _client is null) return;

            // Only the last state of a navigation burst is worth sending.
            _coalesce.Change(CoalesceDelayMs, Timeout.Infinite);
        }
    }

    public void Shutdown()
    {
        lock (_gate)
        {
            _coalesce.Change(Timeout.Infinite, Timeout.Infinite);
            Disconnect();
            SetStateLocked(DiscordConnectionState.Disabled);
        }
    }

    private void PublishLocked()
    {
        lock (_gate) Publish();
    }

    /// <summary>Assigns the state under <see cref="_gate"/> and reports whether
    /// it moved. Callers fire <see cref="ConnectionStateChanged"/> after
    /// releasing the lock, never under it.</summary>
    private bool SetStateLocked(DiscordConnectionState next)
    {
        if (_connection == next) return false;
        _connection = next;
        return true;
    }

    private void OnPipeConnected()
    {
        bool changed;
        lock (_gate)
        {
            _failures = 0;
            changed = SetStateLocked(DiscordConnectionState.Connected);
        }

        if (!changed) return;

        Log.Debug("Discord presence: connected");
        ConnectionStateChanged?.Invoke();
    }

    private void OnPipeFailed()
    {
        lock (_gate)
        {
            if (_connection is DiscordConnectionState.Disabled or DiscordConnectionState.Unreachable) return;
            if (++_failures < MaxConnectionAttempts) return;
            if (!SetStateLocked(DiscordConnectionState.Unreachable)) return;
        }

        Log.Debug($"Discord presence: unreachable after {MaxConnectionAttempts} attempts, waiting for a manual retry");

        // Disposing joins the library's connection thread, and this runs on it.
        Task.Run(() =>
        {
            lock (_gate) Disconnect();
            ConnectionStateChanged?.Invoke();
        });
    }

    public void Dispose()
    {
        Shutdown();
        _coalesce.Dispose();
    }

    private bool Connect()
    {
        var appId = ResolveApplicationId();
        if (string.IsNullOrWhiteSpace(appId))
        {
            Log.Debug("Discord presence: no application id baked in, staying off");
            return false;
        }

        try
        {
            var client = new DiscordRpcClient(appId) { SkipIdenticalPresence = true };
            client.OnConnectionEstablished += (_, _) => OnPipeConnected();
            client.OnConnectionFailed += (_, _) => OnPipeFailed();
            client.OnError += (_, args) => Log.Debug($"Discord presence error: {args.Message}");

            // Initialize only starts the connection thread: it returns true even
            // with no Discord to talk to, so the real state comes from the events.
            client.Initialize();
            _client = client;
            return true;
        }
        catch (Exception ex)
        {
            // Discord being absent or refusing the handshake must never break the app.
            Log.Debug($"Discord presence: failed to initialize ({ex.Message})");
            _client = null;
            return false;
        }
    }

    private void Disconnect()
    {
        if (_client is null) return;

        try
        {
            if (_client.IsInitialized) _client.ClearPresence();
            _client.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug($"Discord presence: shutdown failed ({ex.Message})");
        }
        finally
        {
            _client = null;
        }
    }

    private void Publish()
    {
        if (_client is null) return;

        try
        {
            _client.SetPresence(BuildPresence(_state, Mode, _startedAt));

            // Mode and link state only: the activity text names what is being
            // edited. IsInitialized is deliberately not used here, it stays true
            // even when nothing ever connected.
            Log.Debug($"Discord presence [{Mode}]: published, link={_connection}");
        }
        catch (Exception ex)
        {
            Log.Debug($"Discord presence: update failed ({ex.Message})");
        }
    }

    internal static RichPresence BuildPresence(PresenceState state, DiscordPresenceMode mode, DateTime startedAt)
    {
        var presence = new RichPresence
        {
            Details = Clamp(BuildDetails(state, mode)),
            Timestamps = new Timestamps(startedAt),
            Assets = BuildAssets(state, mode),
        };

        // Minimal mode deliberately says nothing about which game is being modded.
        if (mode != DiscordPresenceMode.Minimal && !string.IsNullOrWhiteSpace(state.GameName))
            presence.State = Clamp(state.GameName);

        return presence;
    }

    /// <summary>
    /// The section is always published: Minimal shows it alone, Game adds the
    /// game as the state line, Detailed adds the item title on top.
    /// </summary>
    private static string BuildDetails(PresenceState state, DiscordPresenceMode mode)
    {
        var title = mode == DiscordPresenceMode.Detailed ? state.ItemTitle?.Trim() : null;
        var hasTitle = !string.IsNullOrEmpty(title);

        if (state.IsUploading)
            return hasTitle ? $"Uploading {title}" : "Uploading to the Workshop";

        return state.Tab switch
        {
            ShellTab.Create => "Creating a new item",
            ShellTab.Settings => "Configuring the app",
            ShellTab.MyMods when hasTitle => $"Editing {title}",
            ShellTab.MyMods => "Browsing Workshop items",
            _ => "Managing Workshop items",
        };
    }

    private static Assets BuildAssets(PresenceState state, DiscordPresenceMode mode)
    {
        // Asset keys are capped at 32 characters by the RPC protocol, so a Steam
        // header URL cannot be used as the image: the game name rides along as
        // the logo's hover text instead.
        var hover = mode != DiscordPresenceMode.Minimal && !string.IsNullOrWhiteSpace(state.GameName)
            ? Clamp(state.GameName)
            : AppName;

        return new Assets
        {
            LargeImageKey = LogoAssetKey,
            LargeImageText = hover,
        };
    }

    private static string Clamp(string value)
    {
        value = value.Trim();
        if (Encoding.UTF8.GetByteCount(value) <= MaxFieldLength) return value;

        // Trim by bytes, not chars, because Discord counts UTF-8 length, and
        // leave room for the ellipsis inside the same budget.
        const string ellipsis = "…";
        var bytes = Encoding.UTF8.GetBytes(value);
        var cut = MaxFieldLength - Encoding.UTF8.GetByteCount(ellipsis);

        // Never cut in the middle of a multi-byte sequence.
        while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;

        return Encoding.UTF8.GetString(bytes, 0, cut).TrimEnd() + ellipsis;
    }

    private static string? ResolveApplicationId()
    {
        var runtime = Environment.GetEnvironmentVariable("SWM_DISCORD_APP_ID");
        if (!string.IsNullOrWhiteSpace(runtime)) return runtime.Trim();

        return typeof(DiscordPresenceService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DiscordAppId")?.Value?.Trim();
    }
}
