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
| `Helpers/` | Small shared utilities: paths, atomic writes, versions, BBCode | Stateless or self-contained |
| `Models/` | Plain data types | No behavior beyond formatting |

## Active session

`ISessionContext` (`Core/Sessions/SessionContext`) holds the session being worked on and its AppId. It is an injected singleton; there is no static global state for the current game.

## Dependency injection

- All registrations are in `Services/ServiceCollectionExtensions.cs`. Services are singletons; per-screen view-models are transient.
- Only the composition roots (`Program`, `App.axaml.cs`) read the service provider. Windows receive their view-model through their constructor.
- View-models that need runtime arguments (the edited item, the upload progress sink) are built with `IViewModelFactory.Create<T>(args)`.

## Editor view-models

The create and edit screens share five child view-models under `ViewModels/Editor/`:

| Child | Responsibility |
| --- | --- |
| `TagEditorViewModel` | Session tag catalog, custom tags, refresh from Steam |
| `VersionRangeViewModel` | Game branches and the min/max range to target |
| `DependencyEditorViewModel` | Required items and apps; staged in create, applied live in edit after `AttachTo` |
| `PreviewGalleryViewModel` | Extra images and videos, reorder, translating edits into preview operations |
| `ChangelogHistoryViewModel` | Past versions, archive download, Steam QR sign-in (edit only) |

`ItemEditorViewModel` and `CreateItemViewModel` own only the item's own fields and delegate the rest. XAML binds through the children, for example `Tags.CustomTags` or `Gallery.ImagePreviews`.

## Publishing flow

`WorkshopOrchestrator` is the single entry point for create, update and delete: it validates, calls the worker through `ISteamService`, records telemetry, stores the local folder fingerprints used for change detection, and applies dependencies after a create.

## Shared update state

`UpdateController` (singleton) holds the update check result, install progress and release notes, shared by the top banner, the Settings page and the "What's new" dialog.
