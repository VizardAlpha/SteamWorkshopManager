# Data and storage

`Helpers/AppPaths` is the single source of truth for every location the app writes to. Add a property there instead of building paths inline.

## Roaming data: `%AppData%/SteamWorkshopManager/`

| Path | Content |
| --- | --- |
| `settings.json` | Global settings (language, toast position, channels, consent version) |
| `telemetry.json` | Pseudonymous instance id, opt-in state and the queue of unsent events |
| `sessions/<id>.json` | One file per session: platform (`steam` or `modio`, missing in files from before 1.9.0 and read as `steam`), game, tag catalog, custom tags, local folders and their fingerprints |
| `bundle/` | Language files extracted from the executable (rewritten only when they change) |
| `tempo/<guid>/draft.json` | Create-form drafts; ids are validated as GUIDs before any file operation |
| `workshop/<appId>/` | Past versions downloaded from the item history |

On macOS and Linux both folders are the platform equivalents returned by .NET for `ApplicationData` and `LocalApplicationData`.

## Machine-local data: `%LocalAppData%/SteamWorkshopManager/`

| Path | Content |
| --- | --- |
| `app_YYYY-MM-DD.log`, `debug_*.log`, `crash_*.log` | Logs, kept 14 days; the debug log is capped at 50 MB per day |
| `credentials.bin` | Steam login tokens, encrypted (see [Security and privacy](security-and-privacy.md)) |
| `updates/` | Downloaded and extracted updates, removed on the next start |
| `tmp/previews/` | Scratch files while re-uploading previews, deleted after each save |

## Cache

Data fetched from a platform lives in one cache shared by every platform (`Services/Cache/CacheStore`), in each OS's own cache folder (`AppPaths.CacheRoot`):

| OS | Folder |
| --- | --- |
| Windows | `%LocalAppData%/SteamWorkshopManager/cache/` |
| macOS | `~/Library/Caches/SteamWorkshopManager/` |
| Linux | `$XDG_CACHE_HOME/SteamWorkshopManager/`, or `~/.cache/SteamWorkshopManager/` |

One file per entry at `<platform>/<scope>/<name>` (`CacheKey`), for example `steam/app-294100/tags.json`. JSON entries carry an envelope with the fetch date and an optional version stamp; images are stored as-is.

| Entry | Key | Freshness |
| --- | --- | --- |
| Store header, library icon | `steam/app-<id>/header.jpg`, `icon.jpg` | Until refreshed by hand |
| Tag catalog | `steam/app-<id>/tags.json` | 7 days |
| App names (dependencies) | `steam/app-<id>/name.json` | 30 days |
| Item and gallery thumbnails | `shared/images/<url hash>` | Keyed by URL, which changes with the image |
| "My mods" list (every field the list and the editor show) | `steam/app-<id>/items-<steamId>.json` | Shown at once, then always replaced by Steam's answer |
| Translations (title and description per language) | `steam/app-<id>/item-<itemId>/text-<language>.json` | Item revision, 7 days at most |
| Mod and app dependencies | `.../item-<itemId>/dependencies.json`, `app-dependencies.json` | Item revision, 1 day at most |
| Changelog | `.../item-<itemId>/changelog.json`, `changelog-signed-in.json` | Item revision, 7 days at most; empty answers not cached |

"Item revision" is Steam's last-update time of the item (`WorkshopItem.Revision`): an entry stored under another revision is a miss. The time cap covers edits made on the Steam website that may not change it. Every add, remove or translation sent from the app drops the matching entry. The changelog is cached apart when signed in, since only that page carries the manifest ids used to download past versions.

The mod list is per Steam account: another login on the same machine has other mods. `ItemListViewModel` shows the cached list with a spinning badge on each card, asks Steam, and merges the answer with `PublishedItemsReconciler`: unchanged items keep their instance (thumbnail, selection), changed ones are swapped, missing ones removed. While a mod is being checked the editor can't save it, so a cached version never overwrites a newer one. If it changed on Steam, the editor reloads it, or warns when the user already edited it.

- `GetOrFetchAsync` serves an entry while its policy says it is fresh, refetches otherwise, and falls back to the expired entry when the refetch fails. Concurrent requests for one key share a single fetch.
- Entries not rewritten for 30 days, and orphan `.tmp` files, are deleted on startup.
- Settings > Storage turns the cache on or off, sets the size limit (500 MB by default, or none) and clears it. Disabled, reads miss and nothing is written.
- When the limit is reached nothing is evicted: writes that would grow the cache are paused and the shell shows a banner until the user raises the limit, disables or clears the cache.
- Mod files are never cached; downloaded versions stay under `workshop/<appId>/`.
- The per-feature folders used before 1.9.0 (`cache/` in the roaming folder, `cache/thumbnails/` in the local one) are deleted on startup.

## How files are written

- State files go through `Helpers/AtomicFile`: content is written to a temporary file then renamed over the target, with a per-path lock. A crash or power loss leaves either the old or the new file, never a truncated one.
- Downloads are written as `.part` and renamed when complete.
- Logs are queued and written by a background thread; errors and process exit flush immediately. User profile paths and registered secrets (account name, SteamID) are redacted from logs and crash logs.

## Deleting a session

`SessionCleanupService` removes the session file, its cache scope (`steam/app-<id>` or `modio/game-<id>`) and, for Steam, downloaded versions and drafts, after showing the user what will go. Data shared with another session for the same game on the same platform is kept.
