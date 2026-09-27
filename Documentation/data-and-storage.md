# Data and storage

`Helpers/AppPaths` is the single source of truth for every location the app writes to. Add a property there instead of building paths inline.

## Roaming data: `%AppData%/SteamWorkshopManager/`

| Path | Content |
| --- | --- |
| `settings.json` | Global settings (language, toast position, channels, consent version) |
| `telemetry.json` | Pseudonymous instance id, opt-in state and the queue of unsent events |
| `sessions/<id>.json` | One file per session: game, tag catalog, custom tags, local folders and their fingerprints |
| `bundle/` | Language files extracted from the executable (rewritten only when they change) |
| `tempo/<guid>/draft.json` | Create-form drafts; ids are validated as GUIDs before any file operation |
| `workshop/<appId>/` | Past versions downloaded from the item history |
| `cache/headers`, `cache/icons`, `cache/tags` | Game header images, session icons, tag catalogs (7-day expiry) |

On macOS and Linux both folders are the platform equivalents returned by .NET for `ApplicationData` and `LocalApplicationData`.

## Machine-local data: `%LocalAppData%/SteamWorkshopManager/`

| Path | Content |
| --- | --- |
| `app_YYYY-MM-DD.log`, `debug_*.log`, `crash_*.log` | Logs, kept 14 days; the debug log is capped at 50 MB per day |
| `credentials.bin` | Steam login tokens, encrypted (see [Security and privacy](security-and-privacy.md)) |
| `updates/` | Downloaded and extracted updates, removed on the next start |
| `cache/thumbnails/` | Item thumbnails, at most 500 files |
| `tmp/previews/` | Scratch files while re-uploading previews, deleted after each save |

## How files are written

- State files go through `Helpers/AtomicFile`: content is written to a temporary file then renamed over the target, with a per-path lock. A crash or power loss leaves either the old or the new file, never a truncated one.
- Downloads are written as `.part` and renamed when complete.
- Logs are queued and written by a background thread; errors and process exit flush immediately. User profile paths and registered secrets (account name, SteamID) are redacted from logs and crash logs.

## Deleting a session

`SessionCleanupService` removes the session file, its caches, downloaded versions and drafts, after showing the user what will go. Data shared with another session for the same game is kept.
