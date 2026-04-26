using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SteamWorkshopManager.Models;
using static SteamWorkshopManager.Services.Core.LocalizationService;

namespace SteamWorkshopManager.Converters;

public class VisibilityTypeConverter : IValueConverter
{
    public static readonly VisibilityTypeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is VisibilityType visibility)
        {
            return visibility switch
            {
                VisibilityType.Public => GetString("Public"),
                VisibilityType.FriendsOnly => GetString("FriendsOnly"),
                VisibilityType.Private => GetString("Private"),
                VisibilityType.Unlisted => GetString("Unlisted"),
                _ => value.ToString()
            };
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
