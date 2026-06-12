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
| `src/Lattice.Ai` | AI module: sensors, condition bitmask world model, FSM/schedule/BT/GOAP/HTN brain tiers, utility needs, groups & collective |
| `src/Lattice.World` | World simulation: clock & calendar, day phases, Markov weather, season overlays, grid A* navigation |
| `src/Lattice.Tooling` | `lattice` CLI: validate, manifest, schemas |
| `schemas/` | Generated JSON schemas (CI fails on drift) |
| `docs/llm-guide.md` | The condensed authoring guide — paste it into a model's context |
| `docs/architecture.md` | Layer diagram, system map, and extension walkthroughs for humans |
| `docs/manifest.md` | Generated content manifest — the dictionary LLMs read before authoring |
| `samples/Lattice.Demo` | Headless console host / development workbench |
| `tests/` | xunit test projects; `Lattice.Demos.Tests` plays the demo scenes over shipped content |
| `content/` | Game content JSON — including the three demo scenes (`scenes.json`) |
| `plan/` | The end-to-end implementation plan (start at `plan/00-overview.md`) |
| `research/` | The emergent-AI research corpus the design is grounded in |

## Install

Every merge to `main` cuts a [GitHub release](https://github.com/Toxic-Cookie/game-lattice/releases)
with all artifacts; see `docs/releasing.md` for how the pipeline works.

| Host | How |
|---|---|
| .NET / NuGet | `dotnet add package GameLattice` (meta-package), or pick `GameLattice.Core` / `.Rpg` / `.Narrative` / `.Ai` / `.World` individually |
| `lattice` CLI | `dotnet tool install -g GameLattice.Tooling` |
| Unity 2021.2+ | OpenUPM `com.gamelattice.lattice`, git URL `https://github.com/Toxic-Cookie/game-lattice.git#upm`, or the `.tgz` from a release (see `packaging/unity/upm/README.md`) |
| Godot 4 .NET | NuGet (preferred), or the `game-lattice-addon-*.zip` from a release / Asset Library (see `packaging/godot/README.md`) |

## Quick start

```bash
dotnet test                                   # build + run all tests (incl. the demo scenes)
dotnet run --project samples/Lattice.Demo     # interactive host shell (kitchen-sink scene)
dotnet run --project samples/Lattice.Demo -- --scene tavern    # or: dungeon | quest
dotnet run --project src/Lattice.Tooling -- validate content
```

## Status

**M7 (demonstration & polish) complete — v0.1.0:** three demo scenes ship as content
(`lifecycle_tavern`, `lifecycle_dungeon`, `lifecycle_quest`) and run headless in CI as
simulation tests over the *shipped* `content/` tree. The Tavern plays a full game day —
the innkeeper's routine flips with the day phases, patrons run needs-driven activity
loops, Charisma haggles bar prices, a look-away meta-sensor interrupts dialogue, and a
patron comments when the rain rolls in — against a golden transcript. The Dungeon
reproduces the F.E.A.R. flanking result (three GOAP soldiers, exclusive attack-node
reservation, distinct nodes held simultaneously), audits the rat problem with the new
planner-invocation counters (`perf` command), rolls loot on kills, ticks a poison trap,
and shows HTN method fallback on a boss (ranged while the arrow lasts, melee after). The
Quest-Giver runs `quest_wolves` end-to-end across a mid-quest save/load. Docs shipped for
both audiences: `docs/llm-guide.md` (the ≤4k-token authoring guide) and
`docs/architecture.md` (layers, seams, extension walkthroughs). Earlier:
**M6 (LLM & modding integration) complete:** blueprint inheritance (`"inherits"` with
deep-merge + explicit `$append`/`$remove` array operators, cross-file chains, cycle/kind
checks); content packs (`pack.json` directories overlay the base content in
priority/dependency order); the `lattice manifest` exporter (every def + the full primitive
vocabularies sourced from `[PrimitiveDoc]` on the executors + catalogs + formula scope, in
markdown or `--json`); matured schemas (`x-lattice-ref` link annotations, primitive-union
discriminators with registered names, `$schema`-header wrapper files, CI drift gate);
rat-problem and GOAP-reachability validation warnings; and path-string UI binding
(`Player.stats.stat_hp`, event-driven, never polled) rendered as console gauges. The
acceptance is literal: LLM-shaped JSON — a spell, a blueprint NPC, a 2-step quest — hot-loads
into a running session and functions, no C# changes, no restart. Earlier:
**M5 (world & environment) complete:** a persisted, deterministic game clock and calendar
publishing `Time.*` events and `Hour`/`Day`/`Season` formula identifiers; day phases that set
`is_night`-style flags (guards sleep, shops close — all data); Markov weather whose states hold
global flags (rain halves auditory range, so stealth weather emerges from a number) and run
tag-scoped effect primitives at boundaries; seasons as registry-redirect overlays (winter swaps
loot tables) that also bias weather; and a deterministic grid A* navigation service with
context-dependent costs (tall grass impassable on patrol, crossable when alert) and
node-reservation splitting. Earlier: **M4 (the full AI suite) complete.** Five data-driven brain tiers sharing one task/condition/
action vocabulary: two-state FSMs, Half-Life condition-gated schedules, behavior trees with
reactive preemption, GOAP (budgeted A*, cost-profile personalities, flanking from smart-object
reservation), and HTN (ordered methods, backtracking decomposition, traced). Above the
individuals: non-physical group agents whose role *slots* produce herd structure, scoped
blackboards whose per-key staleness makes unwitnessed kills genuinely unwitnessed, an
alert-level ladder, a Collective that spawns, budgets, and passport-recycles populations, and
declarative meta-sensors that turn player behavior patterns into ordinary conditions. Debug
commands ship with every system: `agent`, `senses`, `bt`, `utility`, `needs`, `dump`, `trace`,
`groups`, `bb`, `roles`. Plus the M1–M3 stack: registry, events, formulas, persistence, hot
reload, stats/effects, dialogue, quests, and smart objects. See `plan/00-overview.md` §4 for
the roadmap M0–M7.
