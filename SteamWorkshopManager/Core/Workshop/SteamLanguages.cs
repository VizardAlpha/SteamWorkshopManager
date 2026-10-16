using System.Collections.Generic;
using System.Linq;

namespace SteamWorkshopManager.Core.Workshop;

/// <summary>A Steam API language: <see cref="ApiCode"/> goes to Steamworks, <see cref="NativeName"/> to the UI.</summary>
public sealed record SteamLanguage(string ApiCode, string NativeName);

/// <summary>Languages Steam accepts for Workshop titles and descriptions.</summary>
public static class SteamLanguages
{
    /// <summary>Steam assumes this language when an update or query sets none.</summary>
    public const string BaseCode = "english";

    public static IReadOnlyList<SteamLanguage> All { get; } =
    [
        new("arabic", "العربية"),
        new("bulgarian", "Български"),
        new("schinese", "简体中文"),
        new("tchinese", "繁體中文"),
        new("czech", "Čeština"),
        new("danish", "Dansk"),
        new("dutch", "Nederlands"),
        new("english", "English"),
        new("finnish", "Suomi"),
        new("french", "Français"),
        new("german", "Deutsch"),
        new("greek", "Ελληνικά"),
        new("hungarian", "Magyar"),
        new("indonesian", "Bahasa Indonesia"),
        new("italian", "Italiano"),
        new("japanese", "日本語"),
        new("koreana", "한국어"),
        new("norwegian", "Norsk"),
        new("polish", "Polski"),
        new("portuguese", "Português (Portugal)"),
        new("brazilian", "Português (Brasil)"),
        new("romanian", "Română"),
        new("russian", "Русский"),
        new("spanish", "Español (España)"),
        new("latam", "Español (Latinoamérica)"),
        new("swedish", "Svenska"),
        new("thai", "ไทย"),
        new("turkish", "Türkçe"),
        new("ukrainian", "Українська"),
        new("vietnamese", "Tiếng Việt"),
    ];

    public static SteamLanguage Base { get; } = All.First(l => l.ApiCode == BaseCode);

    /// <summary>Every language except the base one.</summary>
    public static IReadOnlyList<SteamLanguage> Translatable { get; } = All.Where(l => l.ApiCode != BaseCode).ToList();

    public static SteamLanguage? Find(string apiCode) => All.FirstOrDefault(l => l.ApiCode == apiCode);
}
