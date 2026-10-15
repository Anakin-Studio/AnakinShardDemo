# Project Layout

Where files belong in AnakinShardDemo. See also the repo layout table in `Development/README.md`.

## Ownership Map

| Path | What belongs there | What does not |
| --- | --- | --- |
| `Assets/` | Compiled engine assets, scenes, prefabs, materials | Source art, PSDs, blend files |
| `Code/` | Runtime C#, Razor, SCSS | Editor tooling, one-off scripts |
| `Editor/` | Editor-only C# | Shipped gameplay logic |
| `Development/` | Docs, references, source assets, dev scenes, scripts | Anything s&box loads at runtime |

## Adding A New System

TODO: which folder new gameplay systems go in, namespace rules, and who to update.

## Rules

- All non-s&box project files go in `Development/`, except `.cursor/` and `.vscode/`.
- Update this doc when adding a top-level folder or changing ownership.
