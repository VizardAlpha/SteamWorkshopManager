using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SteamWorkshopManager.Views.Components;

public partial class ConfirmDialog : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ConfirmDialog, bool>(nameof(IsOpen));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ConfirmDialog, string>(nameof(Title), "Confirmation");

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<ConfirmDialog, string>(nameof(Message), "");

    public static readonly StyledProperty<string> ConfirmTextProperty =
        AvaloniaProperty.Register<ConfirmDialog, string>(nameof(ConfirmText), "Confirmer");

    public static readonly StyledProperty<string> CancelTextProperty =
        AvaloniaProperty.Register<ConfirmDialog, string>(nameof(CancelText), "Annuler");

    public static readonly StyledProperty<IBrush> ConfirmButtonBackgroundProperty =
        AvaloniaProperty.Register<ConfirmDialog, IBrush>(nameof(ConfirmButtonBackground), Brushes.Red);

    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<ConfirmDialog, bool>(nameof(IsLoading));

    public static readonly StyledProperty<ICommand?> ConfirmCommandProperty =
        AvaloniaProperty.Register<ConfirmDialog, ICommand?>(nameof(ConfirmCommand));

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<ConfirmDialog, ICommand?>(nameof(CancelCommand));

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string ConfirmText
    {
        get => GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public string CancelText
    {
        get => GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    public IBrush ConfirmButtonBackground
    {
        get => GetValue(ConfirmButtonBackgroundProperty);
        set => SetValue(ConfirmButtonBackgroundProperty, value);
    }

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public ICommand? ConfirmCommand
    {
        get => GetValue(ConfirmCommandProperty);
        set => SetValue(ConfirmCommandProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public ConfirmDialog()
    {
        InitializeComponent();
    }
}
