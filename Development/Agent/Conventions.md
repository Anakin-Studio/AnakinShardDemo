# Conventions

Shared comment and code style reference:
`C:\Users\Jordan\Documents\s&box projects\1AI DEEP RESEARCH\SBox-Packages\CommentStyle\README.md`

## C# Style

- Match the existing s&box style: Allman braces, tabs, and spaces inside method calls and
  conditions.
- Use `PascalCase` for types, methods, and properties; use `_camelCase` for private fields.
- Components should usually be `sealed`, with `[Title]`, `[Category]`, and `[Icon]`.
- Inspector fields use `[Property]`, grouped with `[Group( "..." )]` when helpful.
- Networked state must be host-authoritative unless there is a clear reason otherwise.

## Project Ownership

- AnakinShardDemo-owned code should live under `AnakinShardDemo.*` namespaces.
- Prefer local extension points, wrappers, or project-specific components over hidden changes to
  copied base files.
- Do not solve gameplay, scene, or UI ownership problems with unrelated hacks.

## UI

- Razor panels live beside their `.razor.scss` files.
- Keep HUD and menu copy short and readable.
- Avoid glossy or generic AI-looking UI text. Use plain labels that fit the game.
- Test Razor in s&box after edits; external build tools do not catch every UI compile issue.

## Files

- Game code: `Code/`
- Engine assets: `Assets/`
- Editor-only code: `Editor/`
- Development docs, source art, references, and one-off scripts: `Development/`
- All non-s&box project files go in `Development/`, except `.cursor/` and `.vscode/`.
- Do not commit `.sbox/` cache files or generated compiled artifacts without a deliberate reason.
