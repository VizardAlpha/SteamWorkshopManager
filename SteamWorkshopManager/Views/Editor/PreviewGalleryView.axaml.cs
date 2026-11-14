using Avalonia;
using Avalonia.Controls;

namespace SteamWorkshopManager.Views.Editor;

public partial class PreviewGalleryView : UserControl
{
    /// <summary>Edit only: marks the entries not yet sent to Steam.</summary>
    public static readonly StyledProperty<bool> ShowPendingUploadProperty =
        AvaloniaProperty.Register<PreviewGalleryView, bool>(nameof(ShowPendingUpload));

    public bool ShowPendingUpload
    {
        get => GetValue(ShowPendingUploadProperty);
        set => SetValue(ShowPendingUploadProperty, value);
    }

    public PreviewGalleryView() => InitializeComponent();
}
