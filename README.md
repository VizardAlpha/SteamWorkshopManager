# Steam Workshop Manager

Built with .NET 10 and Avalonia UI, it works on Windows, macOS, and Linux.


Features:
- Home dashboard: KPI cards plus a list of your recent mods, all on the new top-bar shell
- Smart updates: Change title, description, tags, or visibility without re-uploading your mod content or preview image. The app tracks file path, size, and modification date: only uploads when something actually changed!
- Multi-game support: Manage mods for multiple Steam games (switch between workshops easily)
- Worker-process Steam: Steam runs in a separate process, so switching between sessions does not freeze the UI
- Visual editor: Edit title, description, tags, and preview image — Info / Changelog / Versions tabs in a card-based layout
- Drag & drop: Simply drop your mod folder to create a new item
- Changelog management: add changelogs with each update
- Multi-version support: declare which game versions a mod is compatible with (when supported by the game)
- Real game names: fetched from the Steam Store API instead of bare AppIds
- Auto-fetched tags: Workshop tags are automatically retrieved for each game
- Custom tags: Add your own tags per game
- Multi-language: English, French,... supported
- PlainText or BBCode: Customize your description and changelogs with the tags currently offered by Steam
- Optional usage statistics: opt-in telemetry feeds the public dashboard at [swm-stats.com](https://swm-stats.com) — pseudonymous, no Steam ID, full opt-out from Settings → Privacy


# Requirements

- Steam must be running
- [.NET 10 Runtime (framework-dependent builds)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

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

Languages are stored as `.axaml` resource files in `%AppData%/SteamWorkshopManager/bundle/`. Built-in languages are automatically extracted on first launch. New languages can be added by dropping a file into the bundle folder — no rebuild required.

Want to contribute a translation? See [TRANSLATING.md](TRANSLATING.md) for instructions.


# Privacy

Usage statistics are **opt-in** and pseudonymous: every record is tied to a per-install random UUID with no link to a real-world identity. Telemetry is dispatched only after explicit consent through the setup wizard (new installs) or a privacy-preferences modal (upgrades from previous versions).

What is sent: instance UUID (random, generated locally), operating system + version, application version, UI language, event types (app launch, mod created/updated/deleted, session added), Steam AppId per event, two-letter country code resolved at the Cloudflare edge.

What is **not** sent: Steam IDs, Steam usernames, account information, mod titles or descriptions, file paths, or any personal information.

The aggregated public dashboard lives at [swm-stats.com](https://swm-stats.com). The full policy and the deletion request workflow are documented at [swm-stats.com/Privacy](https://swm-stats.com/Privacy). Telemetry can be toggled at any time from **Settings → Privacy**, and the per-install UUID is displayed there with a copy button so it can be quoted in deletion requests.


## 💙 Support / Donations

This project is free and open-source.

Donations are completely optional and do not unlock any features.
They are simply a way to support development if you wish.

No goods or services are provided in exchange for donations.
Donations are considered personal support and not payments for the software.

If you'd like to support the project:

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/V7V6I9EMH)

Every contribution, feedback, or share helps just as much as donations.

Thank you for your support ❤️