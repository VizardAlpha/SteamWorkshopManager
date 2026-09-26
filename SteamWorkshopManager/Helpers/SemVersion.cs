using System;
using System.Text.RegularExpressions;

namespace SteamWorkshopManager.Helpers;

/// <summary>
/// Minimal SemVer: "v1.8.10", "1.8.10-beta.2", "1.9.0-alpha3". A pre-release sorts
/// before its final version (1.8.10-beta.1 &lt; 1.8.10), labels compare segment by segment
/// with numbers compared numerically (beta.2 &lt; beta.10).
/// </summary>
public sealed partial record SemVersion(Version Core, string? PreRelease) : IComparable<SemVersion>
{
    public bool IsPreRelease => PreRelease is not null;

    public static SemVersion? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var text = input.Trim().TrimStart('v', 'V');
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];

        string? pre = null;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            pre = text[(dash + 1)..];
            text = text[..dash];
            if (pre.Length == 0) return null;
        }

        if (!Version.TryParse(text, out var core)) return null;
        // Normalize 1.8 / 1.8.10.0 so equal versions compare equal.
        core = new Version(core.Major, core.Minor, Math.Max(core.Build, 0));
        return new SemVersion(core, pre);
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;

        var byCore = Core.CompareTo(other.Core);
        if (byCore != 0) return byCore;

        if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null) return -1;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    private static int ComparePreRelease(string a, string b)
    {
        var left = a.Split('.');
        var right = b.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var result = CompareIdentifier(left[i], right[i]);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }

    // "beta10" vs "beta2": compare the text part, then the trailing number.
    private static int CompareIdentifier(string a, string b)
    {
        var ma = IdentifierRegex().Match(a);
        var mb = IdentifierRegex().Match(b);

        var byText = string.Compare(ma.Groups[1].Value, mb.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
        if (byText != 0) return byText;

        var hasA = long.TryParse(ma.Groups[2].Value, out var na);
        var hasB = long.TryParse(mb.Groups[2].Value, out var nb);
        if (hasA && hasB) return na.CompareTo(nb);
        return hasA.CompareTo(hasB);
    }

    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemVersion a, SemVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemVersion a, SemVersion b) => a.CompareTo(b) <= 0;

    public override string ToString() => PreRelease is null ? Core.ToString(3) : $"{Core.ToString(3)}-{PreRelease}";

    [GeneratedRegex(@"^(\D*)(\d*)$")]
    private static partial Regex IdentifierRegex();
}
