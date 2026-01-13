# Steam Workshop Manager

Built with .NET 10 and Avalonia UI, it works on Windows, macOS, and Linux.


Features:
- Smart updates: Change title, description, tags, or visibility without re-uploading your mod content or preview image. The app tracks file path, size, and modification date: only uploads when something actually changed!
- Multi-game support: Manage mods for multiple Steam games (switch between workshops easily)
- Visual editor: Edit title, description, tags, and preview image
- Drag & drop: Simply drop your mod folder to create a new item
- Changelog management: add changelogs with each update
- Auto-fetched tags: Workshop tags are automatically retrieved for each game
- Custom tags: Add your own tags per game
- Multi-language: English and French supported


# Requirements

- Steam must be running
- .NET 10 Runtime (framework-dependent builds)

# Releases

Download the latest release for your platform from the [Releases](https://github.com/VizardAlpha/SteamWorkshopManager/releases) page:
- **Windows**: `SteamWorkshopManager-windows.zip`
- **macOS**: `SteamWorkshopManager-macos.zip`
- **Linux**: `SteamWorkshopManager-linux.zip`

# Build from source

```bash
# Clone
git clone https://github.com/VizardAlpha/SteamWorkshopManager.git
cd SteamWorkshopManager

# Run
dotnet run --project SteamWorkshopManager

# Publish (example for Windows)
dotnet publish SteamWorkshopManager/SteamWorkshopManager.csproj -c Release -r win-x64 --self-contained false -o .dist/windows
```

# Localization

Available languages: English, French

To add a new language, create a new resource file in `Resources/` following the existing pattern.
