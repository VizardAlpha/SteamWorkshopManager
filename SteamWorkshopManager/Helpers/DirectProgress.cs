using System;

namespace WorkshopManager.Helpers;

/// <summary>
/// Reports on the caller's thread, in call order. <see cref="Progress{T}"/> posts each report
/// to the captured context or the thread pool, where a late one can overtake the final one.
/// </summary>
internal sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
