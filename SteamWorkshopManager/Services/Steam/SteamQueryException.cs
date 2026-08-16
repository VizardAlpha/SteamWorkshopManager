using System;

namespace SteamWorkshopManager.Services.Steam;

/// <summary>
/// A Workshop query could not be answered: Steam refused it, never replied, or
/// the client is offline. Deliberately distinct from a query that succeeds with
/// zero results, so the shell can tell "nothing published" from "we do not know".
/// </summary>
public sealed class SteamQueryException(string message) : Exception(message);