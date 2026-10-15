# AnakinShardDemo Developer README

Team-facing hub for understanding the AnakinShardDemo project layout, gameplay loop, and where each
kind of work belongs. Files in `Development/` are not read by s&box at runtime.

## Project Snapshot

TODO: one paragraph describing the game.

- Game title: `AnakinShardDemo`
- Author: `Anakin Studios`
- Project file: `anakinsharddemo.sbproj`
- Package ident: `anakin.anakinsharddemo`
- Startup scene: `scenes/anakinsharddemo.scene`

## How To Play

TODO: numbered steps from launch to the core loop.

## Repo Layout

| Path | Purpose |
| --- | --- |
| `Assets/` | Runtime s&box scenes, prefabs, materials, sounds, models, and other compiled assets. |
| `Code/` | Runtime C#, Razor panels, SCSS, and game systems. |
| `Editor/` | Editor-only C# assembly. No shipped gameplay logic belongs here. |
| `ProjectSettings/` | s&box project settings such as collision, physics, input, and networking. |
| `Localization/` | Localization files. |
| `Development/` | Documentation, planning notes, references, source assets, dev-only scenes, and scripts. |

Non-s&box project files belong in `Development/`. The only root-level exceptions are `.cursor/`
and `.vscode/` workspace files.

## Game Code Layout

TODO: table of `Code/` subfolders and what owns what.

## Development Folder

| Folder | Purpose |
| --- | --- |
| `Docs/` | Project knowledge: architecture, design, scope, progression, and lessons learned. |
| `Agent/` | Working instructions and conventions for AI agents and developers. |
| `References/` | Visual, UI, gameplay, and design references. |
| `Important Source Assets/` | Original source files before export to game-ready assets. |
| `Scenes/` | Archived and dev-only scenes. Not loaded by the shipped game. |
| `Scripts/` | One-off development tools that are not part of the shipped game. |

## Key Docs

- `Docs/ProjectLayout.md`: repo ownership and where files belong.
- `Docs/Architecture.md`: high-level game flow and system ownership.
- `Docs/GameDesign.md`: core loop and gameplay rules.
- `Docs/ProductScope.md`: what is in and out of scope.
- `Docs/Multiplayer.md`: networking model and smoke-test rules.
- `Docs/AdoptedPackages.md`: which SBox-Packages this project copied in, and local changes.
- `Docs/FieldNotes.md`: verified engine findings to fold back into the shared knowledge base.
- `Docs/Versioning.md`: version number and milestone rules.
- `CHANGELOG.md`: public-facing development changelog.
- `Agent/Instructions.md`: agent workflow, compile verification, and investigation order.
- `Agent/VersionControl.md`: changelog and version control rules for code agents.

## Knowledge Base

Shared, cross-project resources. Read before building anything common:

- `C:\Users\Jordan\Documents\s&box projects\1AI DEEP RESEARCH\SBox-Packages\`: portable package
  templates (achievements, votes, chat, inventory, leaderboards, and more). We have built most
  common systems before; copy the package instead of rewriting it, and log it in
  `Docs/AdoptedPackages.md`.
- `C:\Users\Jordan\Documents\s&box projects\1AI DEEP RESEARCH\SBox-Skills\`: engine textbook with
  source-cited topic docs (components, UI, physics, networking, audio, scenes).
- `C:\Users\Jordan\Documents\s&box projects\1AI DEEP RESEARCH\Anakin-SBox-MCP\`: MCP server giving
  agents searchable access to s&box reference repos and docs.

## Team Rules

- Engine assets belong in `Assets/`; runtime game code belongs in `Code/`.
- Source art, references, planning docs, and migration notes belong in `Development/`.
- Update docs when changing ownership of a system, adding a top-level folder, changing multiplayer
  flow, or changing core game rules.
- Update `CHANGELOG.md` when a change affects gameplay, UI, saves, networking, or tools.
