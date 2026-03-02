using static SteamWorkshopManager.Services.LocalizationService;

namespace SteamWorkshopManager.Models;

public class ModVersionInfo
{
    public uint VersionIndex { get; set; }
    public string BranchMin { get; set; } = "";
    public string BranchMax { get; set; } = "";

    public string RangeDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(BranchMin) && string.IsNullOrEmpty(BranchMax))
                return GetString("AllVersions");
            if (string.IsNullOrEmpty(BranchMin))
                return string.Format(GetString("UpTo"), BranchMax);
            if (string.IsNullOrEmpty(BranchMax))
                return string.Format(GetString("AndLater"), BranchMin);
            if (BranchMin == BranchMax)
                return BranchMin;
            return $"{BranchMin} \u2192 {BranchMax}";
        }
    }
}
