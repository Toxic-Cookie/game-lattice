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
| `docs/manifest.md` | Generated content manifest — the dictionary LLMs read before authoring |
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
