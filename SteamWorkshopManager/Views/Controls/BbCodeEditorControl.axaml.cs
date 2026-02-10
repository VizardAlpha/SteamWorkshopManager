using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Views.Controls;

public partial class BbCodeEditorControl : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<BbCodeEditorControl, string>(nameof(Text), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> EditorHeightProperty =
        AvaloniaProperty.Register<BbCodeEditorControl, double>(nameof(EditorHeight), 150);

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<BbCodeEditorControl, string>(nameof(Watermark), string.Empty);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double EditorHeight
    {
        get => GetValue(EditorHeightProperty);
        set => SetValue(EditorHeightProperty, value);
    }

    public string Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    private int _savedSelectionStart;
    private int _savedSelectionEnd;
    private Border? _expandedOverlay;
    private TextBox? _expandedTextBox;

    public BbCodeEditorControl()
    {
        InitializeComponent();
        BuildToolbar();

        BbCodeToggle.IsCheckedChanged += OnToggleChanged;
        EditorTextBox.PropertyChanged += OnEditorTextBoxPropertyChanged;
        ExpandButton.Click += OnExpandClick;

        // Save selection whenever it changes (before button click steals focus)
        EditorTextBox.AddHandler(PointerReleasedEvent, OnEditorPointerReleased, RoutingStrategies.Tunnel);
        EditorTextBox.KeyUp += OnEditorKeyUp;
    }

    private void OnEditorPointerReleased(object? sender, PointerReleasedEventArgs e) => SaveSelection();
    private void OnEditorKeyUp(object? sender, KeyEventArgs e) => SaveSelection();

    private void SaveSelection()
    {
        _savedSelectionStart = EditorTextBox.SelectionStart;
        _savedSelectionEnd = EditorTextBox.SelectionEnd;
    }

    private void OnEditorTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
        {
            var newText = EditorTextBox.Text ?? string.Empty;
            if (Text != newText)
                Text = newText;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            if (EditorTextBox != null && EditorTextBox.Text != Text)
                EditorTextBox.Text = Text;
            // Sync expanded textbox if open
            if (_expandedTextBox != null && _expandedTextBox.Text != Text)
                _expandedTextBox.Text = Text;
        }
        else if (change.Property == EditorHeightProperty)
        {
            if (EditorTextBox != null)
                EditorTextBox.Height = EditorHeight;
        }
        else if (change.Property == WatermarkProperty)
        {
            if (EditorTextBox != null)
                EditorTextBox.Watermark = Watermark;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        EditorTextBox.Height = EditorHeight;
        EditorTextBox.Watermark = Watermark;
        if (EditorTextBox.Text != Text)
            EditorTextBox.Text = Text;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        CollapseEditor();
        base.OnUnloaded(e);
    }

    private void OnToggleChanged(object? sender, RoutedEventArgs e)
    {
        ToolbarPanel.IsVisible = BbCodeToggle.IsChecked == true;
    }

    private void BuildToolbar()
    {
        AddGroup(BbCodeTags.Formatting);
        AddSeparator();
        AddGroup(BbCodeTags.Structure);
        AddSeparator();
        AddGroup(BbCodeTags.Content);
    }

    private static readonly IBrush ButtonBorder = new SolidColorBrush(Color.Parse("#3a4a5a"));
    private static readonly IBrush ButtonForeground = new SolidColorBrush(Color.Parse("#c7d5e0"));

    private static Button CreateTagButton(BbCodeTag tag)
    {
        var btn = new Button
        {
            Content = tag.Label,
            Padding = new Thickness(6, 3),
            FontSize = 11,
            Background = Brushes.Transparent,
            Foreground = ButtonForeground,
            BorderBrush = ButtonBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = false,
        };

        if (tag.Tooltip is not null)
            ToolTip.SetTip(btn, tag.Tooltip);

        return btn;
    }

    private void AddGroup(IReadOnlyList<BbCodeTag> tags)
    {
        foreach (var tag in tags)
        {
            var btn = CreateTagButton(tag);
            btn.Click += (_, _) => InsertTag(tag, EditorTextBox);
            ToolbarButtons.Children.Add(btn);
        }
    }

    private void AddSeparator()
    {
        ToolbarButtons.Children.Add(new Border
        {
            Width = 1,
            Height = 20,
            Background = new SolidColorBrush(Color.Parse("#3a4a5a")),
            Margin = new Thickness(4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    private void InsertTag(BbCodeTag tag, TextBox textBox)
    {
        var text = textBox.Text ?? string.Empty;

        // Use saved selection for the inline editor (buttons are Focusable=false, but just in case)
        int selStart, selEnd;
        if (textBox == EditorTextBox)
        {
            selStart = _savedSelectionStart;
            selEnd = _savedSelectionEnd;
        }
        else
        {
            selStart = textBox.SelectionStart;
            selEnd = textBox.SelectionEnd;
        }

        // Ensure valid range
        if (selStart > text.Length) selStart = text.Length;
        if (selEnd > text.Length) selEnd = text.Length;
        if (selStart > selEnd) (selStart, selEnd) = (selEnd, selStart);

        var selLen = selEnd - selStart;

        if (string.IsNullOrEmpty(tag.CloseTag))
        {
            // Self-closing tag (e.g. HR)
            var newText = text.Insert(selStart, tag.OpenTag);
            textBox.Text = newText;
            textBox.CaretIndex = selStart + tag.OpenTag.Length;
        }
        else if (selLen > 0)
        {
            // Wrap selection
            var selected = text.Substring(selStart, selLen);

            string insertion;
            if (tag.OpenTag.EndsWith('='))
            {
                insertion = $"{tag.OpenTag}{selected}]{selected}{tag.CloseTag}";
            }
            else
            {
                insertion = $"{tag.OpenTag}{selected}{tag.CloseTag}";
            }

            var newText = text.Remove(selStart, selLen).Insert(selStart, insertion);
            textBox.Text = newText;
            textBox.CaretIndex = selStart + insertion.Length;
        }
        else
        {
            // Insert with placeholder
            var inner = tag.Placeholder ?? string.Empty;

            string insertion;
            if (tag.OpenTag.EndsWith('='))
            {
                insertion = $"{tag.OpenTag}]{inner}{tag.CloseTag}";
            }
            else
            {
                insertion = $"{tag.OpenTag}{inner}{tag.CloseTag}";
            }

            var newText = text.Insert(selStart, insertion);
            textBox.Text = newText;
            textBox.CaretIndex = selStart + tag.OpenTag.Length + (tag.OpenTag.EndsWith('=') ? 1 : 0);
        }

        textBox.Focus();

        // Update saved selection to caret position
        _savedSelectionStart = textBox.CaretIndex;
        _savedSelectionEnd = textBox.CaretIndex;
    }

    #region Expand / Collapse

    private void OnExpandClick(object? sender, RoutedEventArgs e)
    {
        if (_expandedOverlay is not null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window window || window.Content is not Panel rootPanel) return;

        // Create expanded textbox
        _expandedTextBox = new TextBox
        {
            Text = Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Watermark = Watermark,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _expandedTextBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
            {
                var newText = _expandedTextBox.Text ?? string.Empty;
                if (Text != newText)
                    Text = newText;
            }
        };

        // Build expanded toolbar
        var expandedToolbar = BuildExpandedToolbar();

        // Close button
        var closeBtn = new Button
        {
            Content = "✕",
            Padding = new Thickness(8, 4),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#c7d5e0")),
            FontSize = 16,
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        closeBtn.Click += (_, _) => CollapseEditor();

        // Header with toolbar + close
        var header = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetColumn(expandedToolbar, 0);
        Grid.SetColumn(closeBtn, 1);
        header.Children.Add(expandedToolbar);
        header.Children.Add(closeBtn);

        // Content panel
        var contentPanel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        contentPanel.Children.Add(header);
        contentPanel.Children.Add(_expandedTextBox);

        // Inner card
        var innerCard = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2a2e33")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Margin = new Thickness(40),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = contentPanel,
            BoxShadow = BoxShadows.Parse("0 8 32 0 #80000000"),
        };

        // Overlay
        _expandedOverlay = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E0000000")),
            Child = innerCard,
            ZIndex = 1000,
        };

        rootPanel.Children.Add(_expandedOverlay);
        _expandedTextBox.Focus();
    }

    private void CollapseEditor()
    {
        if (_expandedOverlay is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Window window && window.Content is Panel rootPanel)
        {
            rootPanel.Children.Remove(_expandedOverlay);
            rootPanel.InvalidateVisual();
        }

        _expandedOverlay = null;
        _expandedTextBox = null;
    }

    private ScrollViewer BuildExpandedToolbar()
    {
        var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        void AddExpandedGroup(IReadOnlyList<BbCodeTag> tags)
        {
            foreach (var tag in tags)
            {
                var btn = CreateTagButton(tag);
                btn.Click += (_, _) =>
                {
                    if (_expandedTextBox is null) return;
                    _savedSelectionStart = _expandedTextBox.SelectionStart;
                    _savedSelectionEnd = _expandedTextBox.SelectionEnd;
                    InsertTag(tag, _expandedTextBox);
                };
                buttonsPanel.Children.Add(btn);
            }
        }

        void AddExpandedSeparator()
        {
            buttonsPanel.Children.Add(new Border
            {
                Width = 1, Height = 20,
                Background = new SolidColorBrush(Color.Parse("#3a4a5a")),
                Margin = new Thickness(4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        AddExpandedGroup(BbCodeTags.Formatting);
        AddExpandedSeparator();
        AddExpandedGroup(BbCodeTags.Structure);
        AddExpandedSeparator();
        AddExpandedGroup(BbCodeTags.Content);

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = buttonsPanel,
        };
    }

    #endregion
}
