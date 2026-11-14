using Avalonia;
using Avalonia.Controls;

namespace SteamWorkshopManager.Views.Editor;

public partial class VersionCompatibilityView : UserControl
{
    /// <summary>Edit only: a branch range needs new content to apply.</summary>
    public static readonly StyledProperty<bool> ShowContentRequiredNoteProperty =
        AvaloniaProperty.Register<VersionCompatibilityView, bool>(nameof(ShowContentRequiredNote));

    public bool ShowContentRequiredNote
    {
        get => GetValue(ShowContentRequiredNoteProperty);
        set => SetValue(ShowContentRequiredNoteProperty, value);
    }

    public VersionCompatibilityView() => InitializeComponent();
}
