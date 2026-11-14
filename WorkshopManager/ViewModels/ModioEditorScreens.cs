using System;
using WorkshopManager.Models;

namespace WorkshopManager.ViewModels;

public sealed class ModioEditorScreens(IViewModelFactory viewModels) : IEditorScreens
{
    public WorkshopPlatform Platform => WorkshopPlatform.ModIo;

    public EditorScreen CreateEditor(WorkshopItem item, IEditorShell shell)
    {
        var editor = viewModels.Create<ModioItemEditorViewModel>(item, shell.UploadProgress);
        // The mod.io editor does not wait for the refreshed item.
        Action<ulong> saved = id => _ = shell.OnItemSavedAsync(id);
        editor.ItemUpdated += saved;
        editor.ItemDeleted += shell.OnItemDeleted;

        return new EditorScreen(editor, () =>
        {
            editor.ItemUpdated -= saved;
            editor.ItemDeleted -= shell.OnItemDeleted;
        });
    }

    public EditorScreen CreateCreator(IEditorShell shell)
    {
        var creator = viewModels.Create<CreateModioItemViewModel>(shell.UploadProgress);
        creator.ItemCreated += shell.OnItemCreated;

        return new EditorScreen(creator, () => creator.ItemCreated -= shell.OnItemCreated);
    }
}
