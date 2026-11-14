using System;
using System.Threading.Tasks;
using WorkshopManager.Models;
using WorkshopManager.Services.Steam;

namespace WorkshopManager.ViewModels;

/// <summary>
/// Builds the create and edit screens of one platform and wires them to the shell.
/// One implementation per platform, picked by the active session: it is the view-model
/// counterpart of <c>IModPlatform</c>, kept here because services must not know view-models.
/// </summary>
public interface IEditorScreens
{
    WorkshopPlatform Platform { get; }

    EditorScreen CreateEditor(WorkshopItem item, IEditorShell shell);

    EditorScreen CreateCreator(IEditorShell shell);
}

/// <summary>What a create or edit screen may ask of the shell, whatever the platform.</summary>
public interface IEditorShell
{
    IProgress<UploadProgress> UploadProgress { get; }

    /// <summary>The mod was saved: refreshes its row and returns the fresh item, null when it could not be fetched.</summary>
    Task<WorkshopItem?> OnItemSavedAsync(ulong id);

    void OnItemDeleted();

    void OnItemCreated(ulong id);
}

/// <summary>A mounted screen and the action that removes the shell's handlers from it.</summary>
public readonly record struct EditorScreen(ViewModelBase View, Action Unhook);
