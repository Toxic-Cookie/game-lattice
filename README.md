# Game Lattice

![Game Lattice](https://raw.githubusercontent.com/Toxic-Cookie/game-lattice/main/media/thumbnail-600x300.png)

[![CI](https://github.com/Toxic-Cookie/game-lattice/actions/workflows/ci.yml/badge.svg)](https://github.com/Toxic-Cookie/game-lattice/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Toxic-Cookie/game-lattice)](https://github.com/Toxic-Cookie/game-lattice/releases)
[![NuGet](https://img.shields.io/nuget/v/GameLattice)](https://www.nuget.org/packages/GameLattice)
[![License](https://img.shields.io/github/license/Toxic-Cookie/game-lattice)](https://github.com/Toxic-Cookie/game-lattice/blob/main/LICENSE)

An engine-agnostic, data-driven RPG framework: **C# provides the engine, JSON provides the soul.**

The C# core is a *fixed interpreter* over a closed vocabulary of effect, condition, and task
primitives. Every gameplay noun — items, spells, quests, NPC brains, weather, economies — is a
JSON *def* that hot-loads into a running session without recompiling. The data formats are
designed to be authorable by LLMs as well as humans: paste
[`docs/llm-guide.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/docs/llm-guide.md)
into a model's context and it can write working content.

## Why it's interesting

- **Content never touches C#.** A new spell, a blueprint NPC variant, or a two-step quest is
  JSON that hot-loads and runs. Adding a new *primitive* is a deliberate, small C# event.
- **Five AI tiers, one vocabulary.** NPC brains scale from two-state FSMs through Half-Life-style
  schedules and reactive behavior trees up to GOAP and HTN planners — all sharing the same
  task/condition verbs, so content can move an NPC between tiers without rewriting it. The
  dungeon demo reproduces the F.E.A.R. flanking result with three GOAP soldiers.
- **Engine-agnostic by construction.** The libraries target `netstandard2.1` (Unity 2021.2+,
  Godot 4 .NET, any modern .NET host) and never reference an engine; hosts implement five small
  interfaces (clock/log, content source, navigation, animation, physics queries).
- **Deterministic and testable.** Everything above the hosting seams runs under a seeded RNG —
  the three demo scenes play out headless in CI as simulation tests against golden transcripts.
- **Built for modding.** Blueprint inheritance with array patches (`$append`/`$remove`),
  content packs that overlay base content, generated JSON schemas for every def kind, and a
  validation CLI with link-pass and AI-reachability warnings.

## What's in the box

| Package | Library | What it does |
|---|---|---|
| `GameLattice.Core` | `Lattice.Core` | Def registry with blueprint + link passes, event bus, lifecycle boot, NCalc formulas with dice, seeded PCG32 RNG, save/load, hosting seams |
| `GameLattice.Rpg` | `Lattice.Rpg` | Stats and derived attributes, status effects, effect/condition primitives, inventory, equipment, loot tables, trade, path-string UI bindings |
| `GameLattice.Narrative` | `Lattice.Narrative` | Yarn + JSON-tree dialogue, event-driven quests, smart objects with reservation |
| `GameLattice.Ai` | `Lattice.Ai` | Profile-calibrated sensors, condition-bitmask world model, the five brain tiers, needs/utility, group agents, the Collective, player-aware meta-sensors |
| `GameLattice.World` | `Lattice.World` | Persisted game clock and calendar, day phases, Markov weather, season overlays, deterministic grid A* with per-profile costs and node reservation |
| `GameLattice` | — | Meta-package that references all five libraries |
| `GameLattice.Tooling` | `Lattice.Tooling` | The `lattice` CLI (dotnet tool): `validate`, `manifest`, `schemas` |

## Install

| Host | How |
|---|---|
| .NET / NuGet | `dotnet add package GameLattice` — or pick individual `GameLattice.*` packages |
| `lattice` CLI | `dotnet tool install -g GameLattice.Tooling` |
| Unity 2021.2+ | OpenUPM `com.gamelattice.lattice`, git URL `https://github.com/Toxic-Cookie/game-lattice.git#upm`, or the `.tgz` from a [release](https://github.com/Toxic-Cookie/game-lattice/releases) — see [`packaging/unity/upm/README.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/packaging/unity/upm/README.md) |
| Godot 4 .NET | NuGet (preferred), or the `game-lattice-addon-*.zip` from a release / Asset Library — see [`packaging/godot/README.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/packaging/godot/README.md) |

Every merge to `main` cuts a [GitHub release](https://github.com/Toxic-Cookie/game-lattice/releases)
automatically — see [`docs/releasing.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/docs/releasing.md).

## Quick start

Working from the repo requires the **.NET 10 SDK** (the shipped libraries themselves target
`netstandard2.1`):

```bash
dotnet test                                   # build + run all tests (incl. the demo scenes)
dotnet run --project samples/Lattice.Demo     # interactive host shell (kitchen-sink scene)
dotnet run --project samples/Lattice.Demo -- --scene tavern    # or: dungeon | quest
dotnet run --project src/Lattice.Tooling -- validate content
```

### Hosting the framework

Modules attach onto a `GameSession` in dependency order, then content loads once:

```csharp
var session   = GameSession.Create(services, LatticeWorld.AddDefTypes(LatticeAi.CreateDefTypes()));
var rpg       = LatticeRpg.Attach(session);
var narrative = LatticeNarrative.Attach(session, rpg);
var ai        = LatticeAi.Attach(session, rpg, narrative);
var world     = LatticeWorld.Attach(session, rpg);   // composition is additive
session.LoadContent();
session.Boot("lifecycle_tavern");                     // a lifecycle def is a bootable scene
```

`services` is a `HostServices` built from five interfaces; standalone implementations
(seeded RNG host, directory content source with hot reload, grid A*, stub animation,
permissive physics) ship in the box, so the framework runs headless with zero engine code.

### Authoring content

Defs are plain JSON — this is the shipped healing potion; effects compose, no item class exists:

```json
{ "id": "item_healing_potion", "type": "item", "name": "Healing Potion",
  "tags": ["consumable"], "basePrice": 12, "consumeOnUse": true,
  "useActions": [ { "type": "Heal", "formula": "10" },
                  { "type": "RemoveStatus", "status": "status_poison" } ] }
```

Formulas are strings evaluated against the live world (`"Str * 2 + 4"`, `"2d6 + 4"`,
`"CAN_SEE_ENEMY * 50"`); cross-references are ids checked by a link pass at load. Start with
[`docs/llm-guide.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/docs/llm-guide.md),
then keep [`docs/manifest.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/docs/manifest.md)
(every registered def and primitive) beside you.

## The demo scenes

Three scenes ship as content (`content/scenes.json`) and double as CI simulation tests:

- **The Tavern** — a full game day: the innkeeper's routine flips with day phases, patrons run
  needs-driven activity loops, Charisma haggles bar prices, a look-away meta-sensor interrupts
  dialogue, and a patron comments when the rain rolls in.
- **The Dungeon** — three GOAP soldiers flank via exclusive attack-node reservation, rat-tier
  critters provably never invoke the planner, kills roll loot tables, a poison trap ticks, and
  an HTN boss falls back from ranged to melee.
- **The Quest-Giver** — `quest_wolves` end-to-end (accept → hunt → report → reward) across a
  mid-quest save/load.

The demo shell exposes the whole machine: `agent`, `senses`, `bt`, `utility`, `needs`, `dump`,
`trace`, `groups`, `bb`, `roles`, `path`, `time`, `events`, `quests`, `perf`, `hud`.

## Documentation

| Doc | Audience |
|---|---|
| [`docs/llm-guide.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/docs/llm-guide.md) | Content authors (human or LLM) — the condensed authoring guide |
| [`docs/manifest.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/docs/manifest.md) | Generated dictionary of every def and primitive vocabulary |
| [`docs/architecture.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/docs/architecture.md) | Framework developers — layers, hosting seams, extension walkthroughs |
| [`docs/releasing.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/docs/releasing.md) | The automated release pipeline and registry setup |
| [`plan/00-overview.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/plan/00-overview.md) | The end-to-end implementation plan (milestones M0–M7) |
| [`research/`](https://github.com/Toxic-Cookie/game-lattice/tree/main/research) | The emergent-AI research corpus the design is grounded in |
| [`CHANGELOG.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/CHANGELOG.md) | What shipped, milestone by milestone |

## Repository layout

| Path | What |
|---|---|
| `src/` | The six shipped projects (`Lattice.Core/.Rpg/.Narrative/.Ai/.World/.Tooling`) |
| `content/` | Game content JSON — including the three demo scenes |
| `schemas/` | Generated JSON schemas, one per def kind (CI fails on drift) |
| `samples/Lattice.Demo` | Headless console host / development workbench |
| `tests/` | xunit projects; `Lattice.Demos.Tests` plays the demo scenes over shipped content |
| `packaging/`, `scripts/` | Unity/Godot package builders used by the release pipeline |
| `docs/`, `plan/`, `research/` | See [Documentation](#documentation) |

## Status

All planned milestones (M0–M7) are complete and shipping as **v0.1.x**: the data backbone,
RPG systems, narrative, the full five-tier AI suite, world simulation, modding/LLM
integration, and the three demo scenes. See the
[CHANGELOG](https://github.com/Toxic-Cookie/game-lattice/blob/main/CHANGELOG.md) for the
detailed history and [`plan/00-overview.md`](https://github.com/Toxic-Cookie/game-lattice/blob/main/plan/00-overview.md)
for what's next.

## License

[Apache-2.0](https://github.com/Toxic-Cookie/game-lattice/blob/main/LICENSE)
