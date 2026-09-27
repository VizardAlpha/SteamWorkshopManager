# Overview

Steam Workshop Manager lets mod authors create, update and manage their Steam Workshop items without each game's own upload tool.

## Who it is for

Modders who publish on the Steam Workshop, often for several games. One app covers every game that has a Workshop: each game is a *session*, and switching sessions switches the game.

## What it solves

- One consistent interface for every game instead of a different uploader per publisher.
- Full item lifecycle: create, update content and metadata, target game versions, manage dependencies and previews, keep a history of past versions, delete.
- Safe defaults: drafts, change detection before upload, confirmations for destructive actions, crash-safe local files.

## Requirements

- Windows, macOS (x64) or Linux, with the .NET 10 runtime (builds are framework-dependent).
- The Steam client running and signed in, with the game owned on that account. Steam in offline mode is detected and reported.
- An install folder you can write to, if you want in-app updates.

## Languages

English and French ship with the app. Community languages can be added without rebuilding, see [TRANSLATING.md](../TRANSLATING.md).

## Project status

Actively developed. Stable releases are tagged `vX.Y.Z`, betas `vX.Y.Z-beta.N`. Planned work includes Mod.io support and a "download all my mods" backup feature.

Steam and the Steam logo are trademarks of Valve Corporation. This project is not affiliated with Valve.
