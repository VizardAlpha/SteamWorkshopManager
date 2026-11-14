using System;
using System.Globalization;
using Avalonia.Data.Converters;
using WorkshopManager.Models;
using static WorkshopManager.Services.Core.LocalizationService;

namespace WorkshopManager.Converters;

public class SteamIdleModeConverter : IValueConverter
{
    public static readonly SteamIdleModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SteamIdleMode mode)
        {
            return mode switch
            {
                SteamIdleMode.StayConnected => GetString("SteamIdleStayConnected"),
                SteamIdleMode.AfterEachAction => GetString("SteamIdleAfterAction"),
                SteamIdleMode.AfterInactivity => GetString("SteamIdleAfterInactivity"),
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
