# Development

## Build and run

- .NET 10 SDK. Open `SteamWorkshopManager.sln` in Rider or Visual Studio, or use the CLI.
- The Steam client must be running to test anything that talks to Steam.
- `dotnet build -o build_test` builds into a separate folder, which avoids DLL locks from a debug session that is still running.
- Useful flag: `--force-setup-wizard` shows the first-run wizard even when a session exists.

## Tests

- MSTest, in `SteamWorkshopManager.Tests/`: `dotnet test SteamWorkshopManager.Tests -o build_test`.
- Tests never hit the network: HTTP calls go through stub handlers and parsers are tested on inline fixtures.
- Covered today: AppId parsing and validation, SemVer and release channels, updater checksums, atomic writes, credential storage, BBCode and Markdown conversion, Workshop tag and changelog parsing, preview ordering, tag selection, session context, Steam error mapping, translation planning.

## Steamworks.NET

Steamworks.NET is not on nuget.org in recent versions, so the app restores it from a local feed, `nuget-local/` (declared in `nuget.config`). The package holds one managed build for Windows and one for Linux/macOS. To move to a new release:

1. In a clone of `rlabrecque/Steamworks.NET`, build `Standalone2.0/Steamworks.NET.Standard.csproj` twice: `-c Windows -p:Platform=x64` and `-c OSX-Linux -p:Platform=x64`.
2. Copy the Windows DLL to `nuget-local/lib`, `ref` and `runtimes/win-x64`, the OSX-Linux DLL to `runtimes/linux-x64` and `runtimes/osx-x64`.
3. Bump the version in `nuget-local/Steamworks.NET.nuspec`, pack it into `nuget-local/` and remove the old `.nupkg`.
4. Replace the native libraries next to the csproj (`steam_api64.dll`, `libsteam_api.so`, `libsteam_api.dylib`) with the ones from the clone's `com.rlabrecque.steamworks.net/Plugins/`. They must match the managed build.
5. Update the version in `SteamWorkshopManager.csproj` and `THIRD-PARTY-NOTICES.md`.

## Conventions

- File-scoped namespaces, nullable enabled, `_camelCase` fields.
- View-models use `[ObservableProperty]` and `[RelayCommand]`, dependencies by constructor. No `App.Services` outside the composition roots.
- Compiled XAML bindings everywhere; a wrong binding path fails the build.
- Never block the UI thread on I/O or RPC. No `async void` except event handlers, and those catch everything.
- New on-disk locations go in `Helpers/AppPaths`; state files are written with `Helpers/AtomicFile`.
- All code, comments, commits and docs in English. Short comments, one line by default.
- User-facing strings live in `Resources/Languages/en-US.axaml` and `fr-FR.axaml` (same keys in both).

## Project layout

```
SteamWorkshopManager/
  Program.cs, App.axaml(.cs)      entry point, composition root, worker and installer modes
  Core/                           domain logic: Sessions, Steam (error mapping), Workshop
  Services/                       adapters: Core (settings, drafts, localization), Log, Notifications
                                  (updates), Session (host, repository), Steam (+ Worker), Telemetry,
                                  UI, Workshop (tags, changelog, downloads, dependencies, versioning)
  ViewModels/                     screens, UpdateController, ViewModelFactory
    Editor/                       child view-models shared by create and edit
  Views/                          windows, views, controls, shell
  Helpers/                        paths, atomic files, SemVer, BBCode, image caches
  Models/                         data types
  Resources/Languages/            en-US, fr-FR
SteamWorkshopManager.Tests/       MSTest suite
Documentation/                    this documentation
```

See [Architecture](architecture.md) for how the layers fit together.
