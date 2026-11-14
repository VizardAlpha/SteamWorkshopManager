using Avalonia;
using Avalonia.Controls;

namespace SteamWorkshopManager.Views.Editor;

public partial class TranslationsView : UserControl
{
    /// <summary>The item's base title, shown as placeholder: it is what an empty translation falls back to.</summary>
    public static readonly StyledProperty<string?> BaseTitleProperty =
        AvaloniaProperty.Register<TranslationsView, string?>(nameof(BaseTitle));

    public string? BaseTitle
    {
        get => GetValue(BaseTitleProperty);
        set => SetValue(BaseTitleProperty, value);
    }

    public TranslationsView() => InitializeComponent();
}
