using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services.Platforms.Modio;

/// <summary>mod.io through the signed-in user's own token, for the active mod.io session.</summary>
public sealed class ModioModPlatform(ModioApiClient client, ModioAccount account, ISessionContext context) : IModPlatform
{
    private const int DeletedStatus = 3;

    public WorkshopPlatform Platform => WorkshopPlatform.ModIo;

    public bool IsReady => account.IsSignedIn && context.Current is ModioSession;

    public string NotReadyMessageKey => "ModioNotSignedIn";

    public ulong AccountId => (ulong)Math.Max(0, account.UserId);

    public async Task<List<WorkshopItem>> GetMyModsAsync()
    {
        var mods = await CallAsync((auth, gameId) => client.GetMyModsAsync(auth, gameId));
        return mods.Where(m => m.Status != DeletedStatus).Select(m => ToItem(m, account.UserId)).ToList();
    }

    public async Task<WorkshopItem?> GetModAsync(ulong id)
    {
        try
        {
            return ToItem(await CallAsync((auth, gameId) => client.GetModAsync(auth, gameId, (long)id)), account.UserId);
        }
        catch (ModioApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<bool> SetVisibilityAsync(ulong id, VisibilityType visibility)
    {
        var visible = visibility == VisibilityType.Public;
        await CallAsync((auth, gameId) => client.EditModAsync(auth, gameId, (long)id, new ModioModEdit(Visible: visible)));
        return true;
    }

    public async Task<bool> DeleteModAsync(ulong id)
    {
        await CallAsync(async (auth, gameId) =>
        {
            await client.DeleteModAsync(auth, gameId, (long)id);
            return true;
        });
        return true;
    }

    /// <summary>Runs a call for the active game; a refused token signs the user out.</summary>
    private Task<T> CallAsync<T>(Func<ModioCredentials, long, Task<T>> call)
    {
        if (context.Current is not ModioSession session)
            throw new InvalidOperationException("No mod.io session is active.");
        return account.RunAsync(auth => call(auth, session.GameId));
    }

    internal static WorkshopItem ToItem(ModioMod mod, long userId) => new()
    {
        Id = (ulong)mod.Id,
        Title = mod.Name,
        Description = mod.Description ?? string.Empty,
        Summary = mod.Summary,
        ProfileUrl = mod.ProfileUrl,
        PreviewImageUrl = mod.Logo?.Thumb320X180 ?? mod.Logo?.Original,
        Visibility = mod.Visible == 1 ? VisibilityType.Public : VisibilityType.Private,
        Tags = mod.Tags.Select(t => new WorkshopTag(t.Name)).ToList(),
        CreatedAt = FromUnix(mod.DateAdded),
        UpdatedAt = FromUnix(mod.DateUpdated == 0 ? mod.DateAdded : mod.DateUpdated),
        SubscriberCount = (ulong)Math.Max(0, mod.Stats?.SubscribersTotal ?? 0),
        FileSize = mod.Modfile?.Filesize ?? 0,
        OwnerId = (ulong)Math.Max(0, mod.SubmittedBy?.Id ?? 0),
        // /me/mods also lists mods the user only helps on as a team member.
        IsOwner = mod.SubmittedBy?.Id == userId,
    };

    private static DateTime FromUnix(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
}
