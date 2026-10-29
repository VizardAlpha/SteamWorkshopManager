using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamWorkshopManager.Services.Platforms.Modio;

/// <summary>A game's mod.io tag groups in the shape sessions store tags.</summary>
public sealed record ModioTagCatalog(Dictionary<string, List<string>> TagsByCategory, List<string> DropdownCategories)
{
    /// <summary>Hidden and locked groups are left out: only the game's admins set those.</summary>
    public static ModioTagCatalog From(IEnumerable<ModioTagOption> options)
    {
        var tags = new Dictionary<string, List<string>>();
        var dropdowns = new List<string>();
        foreach (var option in options.Where(o => !o.Hidden && !o.Locked && o.Tags.Count > 0))
        {
            tags[option.Name] = option.Tags.ToList();
            if (string.Equals(option.Type, "dropdown", StringComparison.OrdinalIgnoreCase))
                dropdowns.Add(option.Name);
        }
        return new ModioTagCatalog(tags, dropdowns);
    }
}
