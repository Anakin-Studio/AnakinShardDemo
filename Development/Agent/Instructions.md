# Agent Instructions

Working notes for AI agents and developers touching AnakinShardDemo.

## s&box compile errors: logs ONLY (mandatory)

Never use `dotnet build` to verify s&box game code. The only source of truth for compile errors is:

`E:\SteamLibrary\steamapps\common\sbox\logs`

Read `sbox-dev.log` (and dated variants if needed). Search for
`Compile of 'anakin.anakinsharddemo' Failed` and `Error |`. Fix every error and re-read the log before
reporting the change as done.

## Editor MCP (mandatory for editor-side changes)

Before any scene, prefab, component, asset, or `.sbproj` change, check that the s&box editor MCP
server is connected (localhost `http://127.0.0.1:7269/mcp`, on by default in the editor). If not
connected, ask to set it up before proceeding. Do not hand-edit scene or prefab JSON blind.

## We have built this before (check packages first)

Before building any common system (votes, chat, achievements, stats, leaderboards, inventory,
toasts, admin tools, spectate, main menu, tab menu, and similar), check the shared package index:

`C:\Users\Jordan\Documents\s&box projects\1AI DEEP RESEARCH\SBox-Packages\README.md`

If a package covers it, copy it in per its README checklist and log it in
`Docs/AdoptedPackages.md`. Do not rewrite systems the package library already hardened.

## Investigation workflow (before any fix)

1. Docs: `Architecture.md`, `Multiplayer.md`, and the doc for the subsystem you are touching.
2. Scenes: for spawn, sync, or missing-object bugs, read the relevant `.scene` files (or use the
   editor MCP) before editing C#.
3. Engine reference: the matching topic doc in
   `C:\Users\Jordan\Documents\s&box projects\1AI DEEP RESEARCH\SBox-Skills\`.
4. Owning code: grep and read the system that already owns the behavior. Do not add parallel paths
   or bandaid cleanup for symptoms caused by wrong scene setup.

No bandaids. Fix root causes (scene objects, guards, missing components).

## Before Editing

- Read `Docs/ProjectLayout.md` and `Docs/Architecture.md` before large changes.
- Read `Docs/GameDesign.md` before changing core gameplay flow.
- Read `Docs/ProductScope.md` before adding UI or menu features.
- Read `Docs/Multiplayer.md` before UI, networking, or scene flow work.
- Read `Agent/VersionControl.md` before changing player-facing behavior.
- Identify which system owns the behavior before editing.

## During Work

- Keep changes narrow and tied to the requested behavior.
- All non-s&box project files go in `Development/` (except `.cursor/` and `.vscode/`).
- Update docs when changing project structure or network flow.
- Update `CHANGELOG.md` when a change affects gameplay, UI, saves, networking, or tools.

## Verification

- After C# or Razor changes: read the s&box log and fix all `Error |` lines. No `dotnet build`.
- For spawn or multiplayer bugs: audit scenes before chasing code.
- Smoke-test the systems you touched before reporting done.
- Record engine findings that contradicted or confirmed the docs in `Docs/FieldNotes.md`.

## Documentation Tone

Clear, direct, useful. Short sections, specific file references. No marketing tone or bloated
AI-style prose.
