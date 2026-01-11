using System.Collections.Generic;
using System.Linq;

namespace SteamWorkshopManager.Models;

public static class WorkshopTags
{
    public static readonly Dictionary<string, List<string>> TagsByCategory = new()
    {
        ["Agriculture"] = ["Aquaculture", "Farms", "Hunting", "Husbandry", "Other"],
        ["Civics"] = ["Food", "Graves", "Health", "Hygiene", "Procreation", "Temples", "Other"],
        ["Infrastructure"] = ["Decoration", "Knowledge", "Law", "Military", "Road", "Torch", "Other"],
        ["Housing"] = ["Furnishings", "Other"],
        ["Logistics"] = ["Depots", "Warehouse"],
        ["Industries"] = ["Crafting", "Mines", "Refining", "Trade Goods"],
        ["Military"] = ["Arms", "Equipments", "Units"],
        ["Technology"] = ["Animal Science", "Civics", "Crafting", "Farming", "Health", "Knowledge", "Mining", "Miscellaneous", "Procreation", "Refining"],
        ["Tweaks and Balance"] = ["Cheats", "Fixes", "Overhauls", "QoL", "Race", "Trait", "Tweaks"]
    };

    public static List<string> GetAllTags() =>
        TagsByCategory.SelectMany(kvp => kvp.Value.Select(v => $"{kvp.Key}: {v}")).ToList();

    public static List<string> GetCategories() =>
        TagsByCategory.Keys.ToList();

    public static List<string> GetTagsForCategory(string category) =>
        TagsByCategory.TryGetValue(category, out var tags) ? tags : [];
}
