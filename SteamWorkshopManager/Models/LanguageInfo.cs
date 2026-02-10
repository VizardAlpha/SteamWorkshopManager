namespace SteamWorkshopManager.Models;

public record LanguageInfo(string Code, string NativeName, string DisplayName, string FilePath)
{
    public string CountryCode => Code.Contains('-') ? Code.Split('-')[1] : Code.ToUpperInvariant();

    public override string ToString() => NativeName;
}
