using System;
using System.Collections.Generic;

namespace SteamWorkshopManager.Services.Platforms.Modio;

/// <summary>
/// A mod.io call failed. Switch on <see cref="ErrorRef"/>, not the message;
/// <see cref="FieldErrors"/> is set on HTTP 422.
/// </summary>
public sealed class ModioApiException(
    int statusCode,
    int errorRef,
    string message,
    IReadOnlyDictionary<string, string>? fieldErrors = null,
    TimeSpan? retryAfter = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public int ErrorRef { get; } = errorRef;
    public IReadOnlyDictionary<string, string>? FieldErrors { get; } = fieldErrors;

    /// <summary>Set when rate limited: no request may be sent before it elapses.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;

    public bool IsRateLimited => StatusCode == 429;

    /// <summary>Token wrong, expired or revoked: the user has to sign in again.</summary>
    public bool IsAuthError => StatusCode == 401;

    /// <summary>mod.io terms changed and must be accepted again on the website.</summary>
    public bool IsTermsRequired => ErrorRef == 11074;
}
