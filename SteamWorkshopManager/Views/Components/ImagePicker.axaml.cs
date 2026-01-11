using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;

namespace SteamWorkshopManager.Views.Components;

public partial class ImagePicker : UserControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<ImagePicker, string?>(nameof(Header));

    public static readonly StyledProperty<string?> ImagePathProperty =
        AvaloniaProperty.Register<ImagePicker, string?>(nameof(ImagePath));

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? ImagePath
    {
        get => GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    public ICommand BrowseCommand { get; }
    public ICommand ClearCommand { get; }

    public ImagePicker()
    {
        BrowseCommand = new AsyncRelayCommand(BrowseAsync);
        ClearCommand = new RelayCommand(Clear);
        InitializeComponent();
    }

    private async Task BrowseAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var window = desktop.MainWindow;
        if (window is null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Sélectionner une image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif"] }
            ]
        });

        if (files.Count > 0)
        {
            ImagePath = files[0].Path.LocalPath;
        }
    }

    private void Clear()
    {
        ImagePath = null;
    }
}
