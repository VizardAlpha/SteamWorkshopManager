# Features

## First launch

- Setup wizard: pick a game by AppId or by pasting its Store or Workshop URL. The app checks the game exists and has a Workshop.
- Consent screen for pseudonymous telemetry and Discord Rich Presence (both off unless you opt in).
- "What's new" window showing the notes of the installed version, once per version.

## Sessions (one per game)

- Session pill in the top bar to switch game; the Steam connection is restarted for the new game.
- Add a session at any time; delete one with a preview of what will be removed (cache, downloaded versions, drafts).
- Each session keeps its tag catalog, custom tags and the local folders you used for each item.

## Home

- Game header image, Steam connection state.
- Totals: number of items, subscribers, total size, last update.
- Most recently updated items.

## My mods

- Grid of your published items with thumbnails and search by title.
- Selection mode for bulk actions: change visibility, or delete (confirmed by typing `DELETE`).

## Item editor

| Section | What you can do |
| --- | --- |
| Info | Title, BBCode description with a Steam-styled preview, preview image, content folder (size shown, changes since last upload highlighted), visibility, tags |
| Changelog | Write the change note sent with the next upload |
| Versions | Target a range of game branches (min / max beta) when the game supports it; see ranges already published |
| History | Past versions from the Workshop changelog, download a past version as a zip (requires a Steam QR sign-in) |
| Dependencies | Required Workshop items (ordered) and required apps (DLC, tools), applied on Steam immediately |
| Previews | Extra images and YouTube videos, drag to reorder; Sketchfab models are shown read-only |

Other editor actions: copy the item id, delete the item (confirmed by typing `DELETE`). Only what changed is uploaded.

## Create an item

- Same sections as the editor; the content folder and image can be dropped onto the window.
- The title is prefilled from the folder name.
- Drafts: save the form and reload it later, per game.
- Dependencies are staged and applied right after the item is published.

## Uploads

- A progress toast shows the current step and bytes sent; navigation is locked while an upload runs.
- An upload only times out when Steam stops reporting progress for 5 minutes, so large items on slow connections finish.
- A failed creation removes the empty item Steam allocated.
- Closing the app during an upload asks for confirmation.

## Settings

| Category | Content |
| --- | --- |
| General | Language |
| Customization | Toast position, Discord Rich Presence detail level (Minimal, Game, Detailed) |
| Privacy | Telemetry toggle, your pseudonymous instance id (for data requests), Discord on/off, Steam account and sign-out |
| Debug | Debug logging, open the log and data folders, log folder size, clear logs |
| Updates | Current version, check for updates, beta channel toggle, install the update, notes of the latest version |
| About | Version, GitHub, changelog, third-party notices |

## Updates

A banner appears when a newer version exists on your channel. "Update and restart" downloads it, verifies its SHA-256 checksum, installs it over the current folder and restarts. See [Updates and releases](updates-and-releases.md).

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+S | Save the edited item, or publish the new one |
| Ctrl+N | Open the create form |
