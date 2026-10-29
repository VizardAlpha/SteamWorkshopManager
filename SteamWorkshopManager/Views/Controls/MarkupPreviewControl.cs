using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;

namespace SteamWorkshopManager.Views.Controls;

/// <summary>Render of the description being edited, styled like the platform's own page.</summary>
public class MarkupPreviewControl : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MarkupPreviewControl, string>(nameof(Text));

    public static readonly StyledProperty<MarkupLanguage> MarkupProperty =
        AvaloniaProperty.Register<MarkupPreviewControl, MarkupLanguage>(nameof(Markup));

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public MarkupLanguage Markup
    {
        get => GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value);
    }

    private readonly ContentControl _host = new();
    private readonly Border _frame;
    private readonly DispatcherTimer _debounce;

    public MarkupPreviewControl()
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };

        _frame = new Border
        {
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
        ApplyFrameColors();
        Content = _frame;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkupProperty)
        {
            ApplyFrameColors();
            Refresh();
        }
        else if (change.Property == TextProperty)
        {
            // Re-rendering on every keystroke is wasteful; settle first.
            _debounce.Stop();
            _debounce.Start();
        }
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

    private void ApplyFrameColors()
    {
        _frame.Background = Markup == MarkupLanguage.Html ? HtmlRenderer.Background : BbCodeRenderer.Background;
        _frame.BorderBrush = Markup == MarkupLanguage.Html ? HtmlRenderer.Outline : BbCodeRenderer.Outline;
    }

    private void Refresh() => _host.Content = string.IsNullOrWhiteSpace(Text)
        ? CreatePlaceholder()
        : Markup == MarkupLanguage.Html ? HtmlRenderer.Render(Text) : BbCodeRenderer.Render(Text);

    private Control CreatePlaceholder() => new TextBlock
    {
        Text = LocalizationService.GetString(Markup == MarkupLanguage.Html ? "HtmlPreviewEmpty" : "BbCodePreviewEmpty"),
        Foreground = Markup == MarkupLanguage.Html ? HtmlRenderer.MutedBrush : BbCodeRenderer.MutedBrush,
        FontStyle = FontStyle.Italic,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
    };
}
