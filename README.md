# Game Lattice

An engine-agnostic, data-driven RPG framework: **C# provides the engine, JSON provides the soul.**

The C# core is an interpreter over a fixed vocabulary of effect, condition, and task primitives;
all game content — items, spells, quests, NPC brains, weather, economies — is declared in JSON and
never requires recompilation. The data formats are designed to be authorable by LLMs as well as humans.

## Layout

| Path | What |
|---|---|
| `src/Lattice.Core` | Core engine seam (netstandard2.1 — Unity/Godot compatible) |
| `src/Lattice.Rpg` | RPG module: stats, status effects, effect/condition primitives, inventory, loot, trade |
| `src/Lattice.Narrative` | Narrative module: Yarn + JSON-tree dialogue, quests, smart-object interactions |
| `src/Lattice.Tooling` | `lattice` CLI: content validation (later: manifest + schema generation) |
| `samples/Lattice.Demo` | Headless console host / development workbench |
| `tests/` | xunit test projects |
| `content/` | Game content JSON |
| `plan/` | The end-to-end implementation plan (start at `plan/00-overview.md`) |
| `research/` | The emergent-AI research corpus the design is grounded in |

## Quick start

```bash
dotnet test                                   # build + run tests
dotnet run --project samples/Lattice.Demo     # interactive host shell
dotnet run --project src/Lattice.Tooling -- validate content
```

## Status

**M3 (narrative & interaction) complete:** YarnSpinner dialogue whose `$variables` are world
state, a machine-friendly JSON dialogue-tree format on the same runner, a step-based quest engine
with event-driven counters, and smart-object interactions with reservation — all composed from
the same effect/condition primitives as M2's stats, statuses, inventory, loot, and trade, on
M1's registry, event bus, formulas, persistence, and hot reload. See `plan/00-overview.md` §4
for the roadmap M0–M7.
