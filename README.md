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
| `src/Lattice.Ai` | AI module: sensors, condition bitmask world model, FSM/schedule/BT/GOAP brain tiers, utility needs |
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

**M4c (GOAP) complete:** data-driven goals, actions, and cost profiles (personality as data)
planned by a budgeted A* over predicate states; F.E.A.R.'s 3-state execution layer
(GoTo → Animate → smart object) with non-interruptible animations blocking decision changes;
smart objects surface as plannable actions whose maxUsers-1 reservations make flanking emerge
from exclusion; all three plan-validation mechanisms plus relevance-filtered replans and goal
hysteresis; every decision reconstructable from the `dump` command. Earlier: M4b behavior trees
+ Sims-pattern utility needs, M4a world model/sensors/FSM/schedules with traced decisions, and
the M1–M3 stack: registry, events, formulas, persistence, hot reload, stats/effects, dialogue,
quests, and smart objects. See `plan/00-overview.md` §4 for the roadmap M0–M7.
