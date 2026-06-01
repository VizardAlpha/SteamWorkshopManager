using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Behaviors;

/// <summary>
/// Reusable drag-and-drop drop target. Attach to any Control:
///   Classes="dropzone"
///   behaviors:DropZone.Accepts="Folder"
///   behaviors:DropZone.Command="{Binding FolderDroppedCommand}"
/// Sets AllowDrop, wires the drag events, toggles the ":dragover" pseudo-class
/// for styling, and invokes Command with a <see cref="DropPayload"/> on a valid
/// drop. Drag events bubble, so dropping over a child of the zone still works.
/// </summary>
public static class DropZone
{
    private const string DragOverClass = ":dragover";

    public static readonly AttachedProperty<DropKinds> AcceptsProperty =
        AvaloniaProperty.RegisterAttached<Control, DropKinds>("Accepts", typeof(DropZone));

    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("Command", typeof(DropZone));

    public static void SetAccepts(Control c, DropKinds v) => c.SetValue(AcceptsProperty, v);
    public static DropKinds GetAccepts(Control c) => c.GetValue(AcceptsProperty);
    public static void SetCommand(Control c, ICommand? v) => c.SetValue(CommandProperty, v);
    public static ICommand? GetCommand(Control c) => c.GetValue(CommandProperty);

    static DropZone()
    {
        AcceptsProperty.Changed.AddClassHandler<Control>(OnAcceptsChanged);
    }

    private static void OnAcceptsChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        // Idempotent: detach first (no-op if not attached), re-attach if a kind
        // is requested. DragEnter shares the DragOver handler.
        DragDrop.RemoveDragEnterHandler(control, OnDragOver);
        DragDrop.RemoveDragOverHandler(control, OnDragOver);
        DragDrop.RemoveDragLeaveHandler(control, OnDragLeave);
        DragDrop.RemoveDropHandler(control, OnDrop);

        if (GetAccepts(control) == DropKinds.None)
        {
            DragDrop.SetAllowDrop(control, false);
            return;
        }

        DragDrop.SetAllowDrop(control, true);
        DragDrop.AddDragEnterHandler(control, OnDragOver);
        DragDrop.AddDragOverHandler(control, OnDragOver);
        DragDrop.AddDragLeaveHandler(control, OnDragLeave);
        DragDrop.AddDropHandler(control, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control control) return;
        e.Handled = true;

        var accept = CanAccept(control, e);
        // Both must be set every time, including the reject branch, or Drop
        // silently won't fire (default DragEffects is None).
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
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

        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;

        var paths = Collect(files, GetAccepts(control), out var matched);
        if (paths.Count == 0) return;

        var command = GetCommand(control);
        var payload = new DropPayload(matched, paths);
        if (command?.CanExecute(payload) == true)
            command.Execute(payload);
    }

    /// <summary>
    /// DragEnter/DragOver acceptance. Cheap format gate first, then refine by
    /// kind. If the platform can't enumerate files yet during the drag (X11 and
    /// macOS only expose them on drop), stay permissive so the copy cursor shows.
    /// </summary>
    private static bool CanAccept(Control control, DragEventArgs e)
    {
        var dt = e.DataTransfer;
        if (!dt.Contains(DataFormat.File)) return false;

        var files = dt.TryGetFiles();
        if (files is null || files.Length == 0) return true;

        return Collect(files, GetAccepts(control), out _).Count > 0;
    }

    private static List<string> Collect(IStorageItem[] files, DropKinds kinds, out DropKinds matched)
    {
        matched = DropKinds.None;
        var result = new List<string>();

        foreach (var item in files)
        {
            if (kinds.HasFlag(DropKinds.Folder) && item is IStorageFolder folder)
            {
                var path = folder.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    result.Add(path);
                    matched = DropKinds.Folder;
                }
            }
            else if (kinds.HasFlag(DropKinds.Images) && item is IStorageFile file)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path) && HasImageExtension(path))
                {
                    result.Add(path);
                    matched = DropKinds.Images;
                }
            }
        }

        return result;
    }

    private static bool HasImageExtension(string path)
    {
        var ext = Path.GetExtension(path);
        foreach (var imageExt in WorkshopMedia.ImageExtensions)
            if (string.Equals(ext, imageExt, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static void SetDragOver(Control control, bool value) =>
        ((IPseudoClasses)control.Classes).Set(DragOverClass, value);
}
