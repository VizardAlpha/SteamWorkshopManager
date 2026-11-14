using CommunityToolkit.Mvvm.Input;
using WorkshopManager.Models;

namespace WorkshopManager.ViewModels;

/// <summary>
/// A screen that creates or edits one mod. It is all the shell knows about such a screen,
/// so a new platform only has to implement it on its own view-models.
/// </summary>
public interface IEditorHost
{
    /// <summary>What Ctrl+S runs; null when the screen has no single save action.</summary>
    IRelayCommand? PrimaryCommand { get; }

    /// <summary>The active session changed. False: the screen belongs to the previous one and must be left.</summary>
    bool TryFollowSession(WorkshopSession session);

    /// <summary>The screen is leaving: release what it holds. True: it left a mod the list does not show yet.</summary>
    bool Close();
}
