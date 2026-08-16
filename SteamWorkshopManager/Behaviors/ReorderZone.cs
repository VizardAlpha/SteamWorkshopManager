using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Behaviors;

/// <summary>
/// In-app drag reorder for list rows. Attach to the row container:
///   Classes="reorder-row"
///   behaviors:ReorderZone.Command="{Binding ReorderPreviewCommand}"
/// Each row is both drag source and drop target: pressing it starts a drag
/// carrying its DataContext, dropping it on another row invokes Command with a
/// <see cref="ReorderRequest"/>. The ":dragover" pseudo-class marks the row
/// under the cursor for styling.
///
/// Buttons inside the row keep their own click behaviour, so the existing
/// move/remove actions still work.
/// </summary>
public static class ReorderZone
{
    private const string DragOverClass = ":dragover";

    // In-process format: the payload never leaves the app, so the item travels as-is.
    private static readonly DataFormat<object> ItemFormat =
        DataFormat.CreateInProcessFormat<object>("swm-reorder-item");

    private static readonly Logger Log = LogService.GetLogger<object>();

    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("Command", typeof(ReorderZone));

    public static void SetCommand(Control c, ICommand? v) => c.SetValue(CommandProperty, v);
    public static ICommand? GetCommand(Control c) => c.GetValue(CommandProperty);

    static ReorderZone()
    {
        CommandProperty.Changed.AddClassHandler<Control>(OnCommandChanged);
    }

    private static void OnCommandChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        // Idempotent, same as DropZone: detach first, re-attach when wired.
        control.PointerPressed -= OnPointerPressed;
        DragDrop.RemoveDragEnterHandler(control, OnDragOver);
        DragDrop.RemoveDragOverHandler(control, OnDragOver);
        DragDrop.RemoveDragLeaveHandler(control, OnDragLeave);
        DragDrop.RemoveDropHandler(control, OnDrop);

        if (GetCommand(control) is null)
        {
            DragDrop.SetAllowDrop(control, false);
            return;
        }

        control.PointerPressed += OnPointerPressed;
        DragDrop.SetAllowDrop(control, true);
        DragDrop.AddDragEnterHandler(control, OnDragOver);
        DragDrop.AddDragOverHandler(control, OnDragOver);
        DragDrop.AddDragLeaveHandler(control, OnDragLeave);
        DragDrop.AddDropHandler(control, OnDrop);
    }

    private static async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        if (control.DataContext is not { } item) return;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(ItemFormat, item));

        try
        {
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            // A cancelled or platform-refused drag must not surface as a crash.
            Log.Debug($"ReorderZone: drag failed ({ex.Message})");
        }
        finally
        {
            SetDragOver(control, false);
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control control) return;
        e.Handled = true;

        var accept = CanAccept(control, e);
        // Both must be set on every path, including the reject branch, or Drop
        // silently won't fire.
        e.DragEffects = accept ? DragDropEffects.Move : DragDropEffects.None;
        SetDragOver(control, accept);
    }

    private static void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Control control) SetDragOver(control, false);
    }

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control control) return;
        e.Handled = true;
        SetDragOver(control, false);

        if (!CanAccept(control, e)) return;

        var source = e.DataTransfer.TryGetValue(ItemFormat);
        if (source is null || control.DataContext is not { } target) return;

        var command = GetCommand(control);
        var request = new ReorderRequest(source, target);
        if (command?.CanExecute(request) == true)
            command.Execute(request);
    }

    private static bool CanAccept(Control control, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(ItemFormat)) return false;

        var source = e.DataTransfer.TryGetValue(ItemFormat);
        return source is not null && !ReferenceEquals(source, control.DataContext);
    }

    private static void SetDragOver(Control control, bool value) =>
        ((IPseudoClasses)control.Classes).Set(DragOverClass, value);
}