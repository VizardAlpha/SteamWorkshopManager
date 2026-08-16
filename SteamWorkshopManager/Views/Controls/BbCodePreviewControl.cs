using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Services.Core;

namespace SteamWorkshopManager.Views.Controls;

/// <summary>Steam-styled render of the BBCode currently in the editor.</summary>
public class BbCodePreviewControl : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<BbCodePreviewControl, string>(nameof(Text));

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private readonly ContentControl _host = new();
    private readonly DispatcherTimer _debounce;

    public BbCodePreviewControl()
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };

        Content = new Border
        {
            Background = BbCodeRenderer.Background,
            BorderBrush = BbCodeRenderer.Outline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 12),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _host,
            },
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != TextProperty) return;

        // Re-rendering on every keystroke is wasteful; settle first.
        _debounce.Stop();
        _debounce.Start();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Refresh();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _debounce.Stop();
        base.OnUnloaded(e);
    }

    private void Refresh() =>
        _host.Content = string.IsNullOrWhiteSpace(Text) ? CreatePlaceholder() : BbCodeRenderer.Render(Text);

    private static Control CreatePlaceholder() => new TextBlock
    {
        Text = LocalizationService.GetString("BbCodePreviewEmpty"),
        Foreground = BbCodeRenderer.MutedBrush,
        FontStyle = FontStyle.Italic,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
    };
}