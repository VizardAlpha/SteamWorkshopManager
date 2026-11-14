using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WorkshopManager.Views.Controls;

/// <summary>
/// Lays its child out on one line at its natural width and, when that does not fit,
/// scrolls it back and forth. Does nothing when the child fits.
/// </summary>
public class Marquee : Decorator
{
    private const double PixelsPerSecond = 30;
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(33);

    /// <summary>Set on a <see cref="Button"/> (globally in App.axaml): its content scrolls when too long.</summary>
    public static readonly AttachedProperty<bool> AutoWrapProperty =
        AvaloniaProperty.RegisterAttached<Marquee, Button, bool>("AutoWrap");

    private static readonly FuncDataTemplate<string> TextTemplate = new((_, _) =>
    {
        var text = new TextBlock();
        text.DataContextChanged += (_, _) => text.Text = text.DataContext as string;
        return new Marquee { Child = text };
    });

    private readonly TranslateTransform _shift = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastTick;
    private TimeSpan _resumeAt;
    private double _overflow;
    private double _offset;
    private int _direction = 1;
    private bool _attached;

    static Marquee()
    {
        ClipToBoundsProperty.OverrideDefaultValue<Marquee>(true);
        AutoWrapProperty.Changed.AddClassHandler<Button>(OnAutoWrapChanged);
    }

    public Marquee()
    {
        _timer = new DispatcherTimer(Frame, DispatcherPriority.Render, (_, _) => Tick());
        _timer.Stop();
    }

    public static bool GetAutoWrap(Button button) => button.GetValue(AutoWrapProperty);
    public static void SetAutoWrap(Button button, bool value) => button.SetValue(AutoWrapProperty, value);

    private static void OnAutoWrapChanged(Button button, AvaloniaPropertyChangedEventArgs e)
    {
        button.Loaded -= OnButtonLoaded;
        if (e.NewValue is true)
        {
            button.Loaded += OnButtonLoaded;
            if (button.IsLoaded) Wrap(button);
        }
    }

    private static void OnButtonLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button) Wrap(button);
    }

    private static void Wrap(Button button)
    {
        // Buttons inside control templates (text box clear button...) are left alone.
        if (!GetAutoWrap(button) || button.TemplatedParent is not null) return;

        switch (button.Content)
        {
            case string when button.ContentTemplate is null:
                button.ContentTemplate = TextTemplate;
                break;
            case Control content and not Marquee when !HandlesOverflowItself(content):
                button.Content = null;
                button.Content = new Marquee { Child = content };
                break;
        }
    }

    // Wrapping or trimmed text already deals with a narrow button: one scrolling line would break it.
    private static bool HandlesOverflowItself(Control content) =>
        content.GetSelfAndVisualDescendants().Any(v =>
            v is TextBox or ScrollViewer or ItemsControl
            || v is TextBlock text
            && (text.TextWrapping != TextWrapping.NoWrap || !Equals(text.TextTrimming, TextTrimming.None)));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty && change.NewValue is Control child)
            child.RenderTransform = _shift;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child) return default;

        child.Measure(availableSize.WithWidth(double.PositiveInfinity));
        var natural = child.DesiredSize;
        return new Size(Math.Min(natural.Width, availableSize.Width), natural.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child) return finalSize;

        var natural = child.DesiredSize.Width;
        child.Arrange(new Rect(0, 0, Math.Max(natural, finalSize.Width), finalSize.Height));

        var overflow = natural - finalSize.Width;
        _overflow = overflow > 0.5 ? overflow : 0;
        if (_overflow == 0) Reset();
        else _offset = Math.Min(_offset, _overflow);
        _shift.X = -_offset;
        UpdateTimer();
        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Reset();
        UpdateTimer();
    }

    private void Reset()
    {
        _offset = 0;
        _direction = 1;
        _shift.X = 0;
    }

    private void UpdateTimer()
    {
        var shouldRun = _attached && _overflow > 0;
        if (shouldRun == _timer.IsEnabled) return;

        if (shouldRun)
        {
            _lastTick = _clock.Elapsed;
            _resumeAt = _lastTick + Pause;
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void Tick()
    {
        var now = _clock.Elapsed;
        var elapsed = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        if (now < _resumeAt || !IsEffectivelyVisible) return;

        _offset += _direction * PixelsPerSecond * elapsed;
        if (_offset >= _overflow)
        {
            _offset = _overflow;
            _direction = -1;
            _resumeAt = now + Pause;
        }
        else if (_offset <= 0)
        {
            _offset = 0;
            _direction = 1;
            _resumeAt = now + Pause;
        }

        _shift.X = -_offset;
    }
}
