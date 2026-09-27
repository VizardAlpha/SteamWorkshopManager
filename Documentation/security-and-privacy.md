# Security and privacy

## Steam credentials

- The app never sees your Steam password. Uploads use the running Steam client through Steamworks.
- Reading item history and downloading past versions needs a Steam web session, obtained by scanning a QR code with the Steam mobile app (SteamKit2).
- The resulting tokens are stored by `SteamCredentialStore` in `%LocalAppData%/SteamWorkshopManager/credentials.bin`: encrypted with DPAPI for the current user on Windows, owner-only file permissions (0600) on macOS and Linux. They are never written to `settings.json` and never logged.
- Tokens from older versions, which lived in plaintext in `settings.json`, are migrated on startup and removed from it.
- Settings > Privacy shows the signed-in account and a sign-out button. To revoke the session on Steam's side as well, remove "SteamWorkshopManager" from Steam's Authorized Devices page.

## Telemetry

- Opt-in only, shown on first launch and toggled in Settings > Privacy. Pseudonymous, not anonymous: events carry a random instance id you can see and quote in a data request.
- Sent to `https://swm-stats.com`: instance id, OS family and version, app version, language, event type (app start, item created / updated / deleted, session added), the game's AppId, timestamp. The server adds a two-letter country code resolved at the Cloudflare edge; the IP address itself is not stored.
- Never sent: SteamID, account name, item titles or descriptions, file paths.
- Full policy and deletion requests: [swm-stats.com/Privacy](https://swm-stats.com/Privacy).
- The endpoint is baked in at build time and can be overridden with the `SWM_TELEMETRY_URL` environment variable.

## Discord Rich Presence

Off by default. When enabled, you choose how much is shown: Minimal (the app only), Game (plus the game) or Detailed (plus the item being edited).

## Process isolation

- The Steam worker pipe accepts only the current user (`PipeOptions.CurrentUserOnly`).
- The worker is started from the app's own executable with fixed arguments, without a shell.

## Links and remote content

- Only `http` and `https` links are opened from descriptions; the host is shown next to off-site links.
- Release pages and update downloads are only accepted from this repository's GitHub URLs.
- BBCode previews are rendered as native controls, not HTML.

## Updates and builds

- Every release publishes `SHA256SUMS.txt`; the in-app updater refuses a package whose checksum does not match.
- CI actions are pinned to commit SHAs, the workflow token is read-only except for the release job, and Dependabot keeps the actions up to date.
- Builds are not code-signed yet.

## Files

State files are written atomically, draft and session ids are validated before any file operation, and temporary preview files live in a per-user folder rather than a shared temp directory.

## Reporting a vulnerability

Please open a private security advisory on the GitHub repository rather than a public issue.
