using System;
using System.Threading.Tasks;
using Steamworks;
using WorkshopManager.Models;

namespace WorkshopManager.ViewModels;

public sealed class SteamEditorScreens(IViewModelFactory viewModels) : IEditorScreens
{
    public WorkshopPlatform Platform => WorkshopPlatform.Steam;

    public EditorScreen CreateEditor(WorkshopItem item, IEditorShell shell)
    {
        var editor = viewModels.Create<ItemEditorViewModel>(item, shell.UploadProgress);
        Func<PublishedFileId_t, Task<WorkshopItem?>> saved = id => shell.OnItemSavedAsync(id.m_PublishedFileId);
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
        var creator = viewModels.Create<CreateItemViewModel>(shell.UploadProgress);
        Action<PublishedFileId_t> created = id => shell.OnItemCreated(id.m_PublishedFileId);
        creator.ItemCreated += created;

        return new EditorScreen(creator, () => creator.ItemCreated -= created);
    }
}
