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

1. Downloads the platform zip (`SteamWorkshopManager-windows|macos|linux.zip`) and `SHA256SUMS.txt`, both only from this repository.
2. Verifies the zip's SHA-256; a mismatch aborts the install.
3. Extracts it under `%LocalAppData%/SteamWorkshopManager/updates/<version>/`.
4. Starts the extracted executable with `--apply-update --target <install folder> --pid <current process>` and closes the app.
5. The new copy waits for the old process to exit, copies itself over the install folder (retrying while the worker releases files), then restarts the app. On failure it restarts the previous version.

The update button falls back to opening the release page when the release has no checksums, the install folder is not writable, or the app runs through `dotnet`. In-app updates work from 1.8.10 onward.

## Release notes in the app

- The "What's new" window shows the notes of the installed version, once per version (first launch included).
- Settings > Updates shows the notes of the latest release on the user's channel.
- Both read the GitHub release body (Markdown, converted to BBCode for display), so paste the changelog into the release.