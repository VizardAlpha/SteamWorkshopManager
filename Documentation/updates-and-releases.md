# Updates and releases

## Versions and channels

| Tag | Channel | GitHub release |
| --- | --- | --- |
| `vX.Y.Z` | Stable | Normal release |
| `vX.Y.Z-beta.N` (also `-alpha.N`, `-rc.N`) | Beta | Flagged pre-release automatically by CI |

- Versions compare as SemVer (`Helpers/SemVersion`): a pre-release sorts before its final version (`1.8.10-beta.1 < 1.8.10`), and numbers inside labels compare numerically (`beta.2 < beta.10`).
- The stable channel only sees releases that are neither flagged pre-release nor suffixed, so a beta tagged by mistake never reaches stable users.
- The beta channel is the "Include pre-releases" toggle in Settings > Updates.
- In the csproj, `<Version>` carries the suffix (it becomes the app's displayed version); `AssemblyVersion` and `FileVersion` stay four plain numbers.

## In-app updater

`UpdateCheckerService` reads the repository's releases and picks the highest one on the user's channel. `AppUpdater` then:

1. Downloads the platform zip (`WorkshopManager-windows|macos|linux.zip`) and `SHA256SUMS.txt`, both only from this repository.
2. Verifies the zip's SHA-256; a mismatch aborts the install.
3. Extracts it under `%LocalAppData%/SteamWorkshopManager/updates/<version>/`.
4. Starts the extracted executable with `--apply-update --target <install folder> --pid <current process>` and closes the app.
5. The new copy waits for the old process to exit, copies itself over the install folder (retrying while the worker releases files), then restarts the app. The copy is all or nothing: each replaced file is first moved to `.update-backup/` and put back if any step fails, so a failure restarts the previous version intact. A successful update writes `update-manifest.txt` (the files it installed); the next update uses it to delete files the new version no longer ships, and never touches files it did not install.

The update button falls back to opening the release page when the release has no checksums, the install folder is not writable, or the app runs through `dotnet`. In-app updates work from 1.8.10 onward.

The app was renamed from Steam Workshop Manager to Workshop Manager in 1.9.0, with the repository, the binaries and the release zips. Versions before 1.9.0 only trust assets of the old repository URL, so their update button opens the release page and that one update is installed by hand. On startup the app removes leftover `SteamWorkshopManager.*` binaries from its folder (`Helpers/LegacyBinaries`). The data folders keep the historical `SteamWorkshopManager` name.

## Release notes in the app

- The "What's new" window shows the notes of the installed version, once per version (first launch included).
- Settings > Updates shows the notes of the latest release on the user's channel.
- Both read the GitHub release body (Markdown, converted to BBCode for display), so paste the changelog into the release.