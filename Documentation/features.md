# Features

## First launch

- Setup wizard: pick a game by AppId or by pasting its Store or Workshop URL. The app checks the game exists and has a Workshop.
- Consent screen for pseudonymous telemetry and Discord Rich Presence (both off unless you opt in).
- "What's new" window showing the notes of the installed version, once per version.

## Sessions (one per game and platform)

- Session pill in the top bar to switch game; the Steam connection is restarted for the new game.
- Add a session at any time, for a Steam Workshop game or a mod.io game (sign in to mod.io with your own token first); delete one with a preview of what will be removed (cache, downloaded versions, drafts).
- Each session keeps its tag catalog, custom tags and the local folders you used for each item.
- On a mod.io session, "My mods" lists your mods for that game (public or hidden) with bulk visibility and delete; if mod.io stops accepting your token, the list asks you to sign in again.
- Create a mod.io mod in three steps, like on mod.io: Details (logo, name, summary, description, homepage, visibility, comments and collections, tags) creates the mod, then Media (gallery images, YouTube, Sketchfab) and File (a folder dropped or picked, zipped, with version and changelog) can each be skipped.
- When the game allows dependencies, a Dependencies section lists the mods this one needs, finds and adds others from the game, removes them, and (on opt-in or opt-out games) lets other mods depend on this one.
- The File section of a mod.io mod lists every published file: download it (checked against mod.io's checksum), make an older one live again without re-uploading, edit its version and changelog, or delete it.
- mod.io descriptions are written in HTML with the same editor as Steam's BBCode (toolbar, expand, preview), switched to HTML tags and a mod.io-styled preview; the counter counts the HTML sent, tags included.
- A mod.io mod opens in its own editor with the same sections (Details, File, Media), the logo preview and a link to its mod.io page; one save sends what changed. Logo, gallery images and the folder can be dragged in. Saving is blocked while the cached mod is being checked on mod.io.
- The tag catalog is the developer's tag list, read from the game's Workshop page. When the developer set none, Steam lists every tag any upload ever set instead; the app then shows no catalog and tags are added as custom tags.

## Home

- Game header image, Steam connection state.
- Totals: number of items, subscribers, total size, last update.
- Most recently updated items.

## My mods

- Grid of your published items with thumbnails and search by title. The grid is virtualized: only visible cards are created and fetch their thumbnail, so large accounts stay fast.
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
| Translations | Title and description per Steam language (the Info section holds the English base); sent on save, one submit per changed language |

Other editor actions: copy the item id, delete the item (confirmed by typing `DELETE`). Only what changed is uploaded.

### Translations

- Opening the section checks, one query per language, which of the 29 Steam languages already have a translation. Full descriptions are compared, since translations often share the title and the opening lines with the base (about 5 KB per language). The result stays in memory while the editor is open; nothing is cached on disk.
- An empty title or description in a translation falls back to the base text when sent. The app never sends an empty title.
- Steam cannot delete a translation, neither through the API nor on its website, so the editor can only drop a language that was never sent. To make a language show the base text, copy the base into it and save. Sending empty texts does not delete it either: Steam keeps a blank translation and visitors in that language see no title and no description. The editor flags such blank translations so they can be rewritten.

## Create an item

- Same sections as the editor; the content folder and image can be dropped onto the window.
- The title is prefilled from the folder name.
- Drafts: save the form and reload it later, per game.
- Dependencies are staged and applied right after the item is published.
- Translations are staged too (and kept in drafts), then sent one language at a time right after the item is published. A language that fails is reported; it can be added again from the item's Translations section.

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
| About | Version, GitHub, official Discord, changelog, third-party notices |

## Updates

A banner appears when a newer version exists on your channel. "Update and restart" downloads it, verifies its SHA-256 checksum, installs it over the current folder and restarts. See [Updates and releases](updates-and-releases.md).

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+S | Save the edited item, or publish the new one |
| Ctrl+N | Open the create form |
