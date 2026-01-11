using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Views.Components;

public partial class TagSelector : UserControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<TagSelector, string?>(nameof(Header));

    public static readonly StyledProperty<ObservableCollection<WorkshopTag>?> TagsProperty =
        AvaloniaProperty.Register<TagSelector, ObservableCollection<WorkshopTag>?>(nameof(Tags));

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public ObservableCollection<WorkshopTag>? Tags
    {
        get => GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    public TagSelector()
    {
        InitializeComponent();
    }
}
