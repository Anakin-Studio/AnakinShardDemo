# AnakinShardDemo Agent Prompt

Self-contained prompt for low-token agents working on this project. Keep it short enough to paste
whole. Update when facts, rules, or traps change.

## Project Facts

- Game: AnakinShardDemo (anakin.anakinsharddemo), s&box, project file `anakinsharddemo.sbproj`.
- TODO: one line on the core loop.
- TODO: startup scene and scene flow.
- TODO: key systems and the folders that own them.

## Hard Rules

- Compile verification: read `E:\SteamLibrary\steamapps\common\sbox\logs\sbox-dev.log` and fix all
  `Error |` lines. Never `dotnet build`.
- Editor-side changes (scene, prefab, asset, `.sbproj`) go through the editor MCP, not blind JSON
  edits.
- Non-s&box files go in `Development/`.
- Check `C:\Users\Jordan\Documents\s&box projects\1AI DEEP RESEARCH\SBox-Packages\README.md` before
  building common systems; log adoptions in `Docs/AdoptedPackages.md`.
- TODO: project-specific hard rules.

## Known Traps

- TODO: list traps as they are discovered. Seed from `Docs/FieldNotes.md`.

## Workflow

1. Read the owning system before editing.
2. Make the narrow change.
3. Read the s&box log; fix every error.
4. Smoke-test the touched system.
5. Update `CHANGELOG.md` if player-facing.

## Report Format

State what changed, which files, what the log showed, and what was smoke-tested. Plain text, no
marketing tone.
