using System;
using System.Threading;

namespace SteamWorkshopManager.Services.UI;

/// <summary>
/// Links the upload toast's Cancel button to the upload that is running. Only uploads
/// that can really be stopped open a scope: mod.io requests can be aborted, a Steam
/// submission cannot (Steamworks has no cancel call).
/// </summary>
public sealed class UploadCancellation
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _current;

    /// <summary>Raised when an upload becomes cancellable or stops being so. Any thread.</summary>
    public event Action? Changed;

    public bool CanCancel
    {
        get { lock (_gate) return _current is { IsCancellationRequested: false }; }
    }

    /// <summary>Opens a cancellable upload; dispose the scope when the upload ends.</summary>
    public Scope Begin()
    {
        var cts = new CancellationTokenSource();
        lock (_gate) _current = cts;
        Changed?.Invoke();
        return new Scope(this, cts);
    }

    public void Cancel()
    {
        lock (_gate) _current?.Cancel();
        Changed?.Invoke();
    }

    private void End(CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_current, cts)) _current = null;
        }
        cts.Dispose();
        Changed?.Invoke();
    }

    public sealed class Scope(UploadCancellation owner, CancellationTokenSource cts) : IDisposable
    {
        private bool _disposed;

        public CancellationToken Token { get; } = cts.Token;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.End(cts);
        }
    }
}
