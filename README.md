# Codex Thread Reader

Codex Thread Reader is a read-only Windows WPF tool for recovering and exporting local Codex Desktop threads, including threads that no longer appear in the Codex Desktop sidebar or search.

## What It Reads

By default the app reads:

- `%USERPROFILE%\.codex\state_5.sqlite`
- `%USERPROFILE%\.codex\session_index.jsonl`
- `%USERPROFILE%\.codex\sessions\**\rollout-*.jsonl`
- `%USERPROFILE%\.codex\archived_sessions\**\rollout-*.jsonl`

It does not write to `.codex`, repair Codex state, or call Codex APIs.

## Features

- Enumerates active, archived, hidden, and rollout-only threads.
- Groups threads by Codex project root, with anchored threads sorted first inside their project.
- Shows recovery flags such as `Anchored`, `Archived`, `MissingFromSessionIndex`, `ExtendedPath`, `RolloutOnly`, `DbOnlyMissingFile`, and `LargeRollout`.
- Lets you persistently anchor a thread ID at the top of the list even when Codex Desktop cannot show it.
- Resolves sidebar titles from Codex's session index or rollout title events before falling back to compacted SQLite text.
- Supports System, Light, and Dark theme modes.
- Streams rollout JSONL so very large thread files are not loaded as raw text.
- Exports selected threads as:
  - `thread.html`
  - `thread.normalized.json`
  - `thread.raw.jsonl`
  - `handoff.md`
- Copies the handoff prompt to the clipboard after export.

## Build

```powershell
dotnet test .\CodexThreadReader.slnx
dotnet publish .\src\CodexThreadReader\CodexThreadReader.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o .\publish
```

Run:

```powershell
.\publish\CodexThreadReader.exe
```

## Privacy Boundary

Thread exports contain private conversation content, local paths, command output, and potentially secrets. The repository `.gitignore` excludes JSONL, SQLite, export folders, and `.codex` data. Do not commit generated exports.
