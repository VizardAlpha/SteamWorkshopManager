using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services;

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
                VisibilityType.Public => LocalizationService.GetString("Public"),
                VisibilityType.FriendsOnly => LocalizationService.GetString("FriendsOnly"),
                VisibilityType.Private => LocalizationService.GetString("Private"),
                VisibilityType.Unlisted => LocalizationService.GetString("Unlisted"),
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
