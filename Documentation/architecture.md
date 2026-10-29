# Architecture

.NET 10, C# 14, Avalonia 12 with compiled bindings, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection.

## Two processes

```
Shell process (the window)                      Worker process (one per session)
  Views -> ViewModels -> Core -> Services  ==>  SteamWorkerImpl -> SteamService -> Steam client
                                   |    JSON-RPC over a named pipe
                                   +--> HTTPS: Steam Store / Community, GitHub, swm-stats.com
```

The shell never calls Steamworks. Every Steam operation goes through `SessionHost` to a child process started from the same executable with `--steam-worker`. Details in [Steam worker](steam-worker.md).

## Layers

| Folder | Role | Rule |
| --- | --- | --- |
| `Views/` | Avalonia XAML and minimal code-behind | No logic; bindings are compiled and checked at build time |
| `ViewModels/` | Presentation state and commands | Consume Core and Services through constructor injection |
| `Core/` | Domain logic and use cases: sessions, the Workshop orchestrator, validators, preview-op building | No direct HTTP, file-format or RPC code |
| `Services/` | Adapters: HTTP, files, worker RPC, OS, SteamKit2 | Anything that talks to the network or the worker lives here |
| `Helpers/` | Small shared utilities: paths, atomic writes, versions, BBCode and HTML parsing and rendering | Stateless or self-contained |
| `Models/` | Plain data types | No behavior beyond formatting |

## Active session

`ISessionContext` (`Core/Sessions/SessionContext`) holds the session being worked on, its platform and, for Steam, its AppId. It is an injected singleton; there is no static global state for the current game.

A session is bound to one platform for good: `WorkshopSession` is abstract, with `SteamSession` (AppId) and `ModioSession` (mod.io game id). Only Steam sessions start a worker (`SessionHost.StartSessionAsync(session)`); switching to another platform stops it. Two sessions share game data (cache, downloads, drafts) when `IsSameGame` says they target the same game on the same platform.

The screens every platform shares (home, "My mods", bulk visibility and delete) talk to `IModPlatform` (`Services/Platforms/`), never to `ISteamService`. `ModPlatforms.Current` returns the implementation for the active session (`SteamModPlatform` wraps the worker). Items carry a neutral `WorkshopItem.Id`; `PublishedFileId` is its Steam view for the Steam-only editor and creation flows.

mod.io is plain HTTPS from the shell (`Services/Platforms/Modio/`): `ModioApiClient` takes the user's `ModioCredentials` on every call, pages through lists, uploads files in one request or in 50 MiB parts above 100 MB, and stops sending after a 429 until the retry delay elapses. `ModioContentArchive` builds the zip mod.io requires, without OS metadata files. `ModioModPlatform` serves the shared screens for mod.io sessions: "My mods" is `/me/mods` for the game (deleted mods left out), visibility is public or hidden. A refused token signs the user out (`ModioAccount.Expire`); the list then shows the sign-in card and the footer says so. The mod.io editor and create flow follow mod.io's own pages, with three section view-models under `ViewModels/Modio/` shared by both: `ModioDetailsViewModel` (logo, name, summary, HTML description, homepage, public or hidden, comments and collections, catalog tags), `ModioMediaViewModel` (gallery images, YouTube, Sketchfab) and `ModioFileViewModel` (folder zipped as the new live file). `ModioItemEditorViewModel` reads the full mod on open and one save sends what changed in any section through `ModioPublisher`; tags outside the catalog (set by the game's admins) and community bits the form doesn't show are never touched. The File section also lists the published files (`GET /files`): download through a fresh `binary_url` (it can expire) to `workshop/modio-<gameId>/`, MD5-checked, without sending the token to the CDN; make one live (`active`), edit its notes, delete it. `ModioDependenciesViewModel` appears when the game's `dependency_option` allows dependencies; adding and removing apply at once, while the "other mods may depend on this one" choice (community bit 1024, opt-in and opt-out games only) is saved with the editor. Reads (game, mod, files, dependencies) are cached per mod revision and dropped by every write from `ModioPublisher`. `CreateModioItemViewModel` is a 3-step wizard: Details creates the mod, then Media and File can each be skipped. Both views share `Themes/ModioEditorStyles.axaml`.

## Dependency injection

- All registrations are in `Services/ServiceCollectionExtensions.cs`. Services are singletons; per-screen view-models are transient.
- Only the composition roots (`Program`, `App.axaml.cs`) read the service provider. Windows receive their view-model through their constructor.
- View-models that need runtime arguments (the edited item, the upload progress sink) are built with `IViewModelFactory.Create<T>(args)`.

## Editor view-models

The create and edit screens share child view-models under `ViewModels/Editor/`:

| Child | Responsibility |
| --- | --- |
| `TagEditorViewModel` | Session tag catalog, custom tags, refresh from Steam |
| `VersionRangeViewModel` | Game branches and the min/max range to target |
| `DependencyEditorViewModel` | Required items and apps; staged in create, applied live in edit after `AttachTo` |
| `PreviewGalleryViewModel` | Extra images and videos, reorder, translating edits into preview operations |
| `ChangelogHistoryViewModel` | Past versions, archive download, Steam QR sign-in (edit only) |
| `TranslationEditorViewModel` | Per-language titles and descriptions, sent after the main submit (staged for create, reads Steam after `AttachTo` for edit) |

`ItemEditorViewModel` and `CreateItemViewModel` own only the item's own fields and delegate the rest. XAML binds through the children, for example `Tags.CustomTags` or `Gallery.ImagePreviews`.

## Shell view-models

`MainViewModel` only handles navigation, session switching and worker recovery. The shell widgets live in child view-models under `ViewModels/Shell/`, built through the factory:

| Child | Responsibility |
| --- | --- |
| `ToastViewModel` | Success/error toasts from `INotificationService`, auto-hide, docking corner |
| `UploadProgressViewModel` | Upload toast and the `IProgress<UploadProgress>` sink handed to the editors |
| `ConnectionStatusViewModel` | Footer connection dot and status line, mapped from `SteamInitResult` |
| `SessionListViewModel` | Sessions of the top-bar pill and flyout, game icon loading |
| `SessionDeletionViewModel` | Type-to-confirm session deletion and the cleanup report |

## Publishing flow

`WorkshopOrchestrator` is the single entry point for create, update and delete: it validates, calls the worker through `ISteamService`, records telemetry, stores the local folder fingerprints used for change detection, and applies dependencies after a create.

## Shared update state

`UpdateController` (singleton) holds the update check result, install progress and release notes, shared by the top banner, the Settings page and the "What's new" dialog.
