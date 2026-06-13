# Changelog

## Unreleased

- Unity UPM package: stop bundling `Microsoft.CSharp.dll`. The Unity editor references
  its own copy in every compilation, so the bundled one made any consuming project fail
  with CS1703 ("multiple assemblies with equivalent identity") until it was deleted by
  hand. Unity's copy satisfies the dependency closure at runtime.

## 0.1.0 — 2026-06-11

First complete vertical slice: the framework interprets, the content plays, the docs teach.

### M7 — Demonstration & polish
- Three demo scenes shipped as content (`content/scenes.json`) and played headless in CI by
  `tests/Lattice.Demos.Tests` over the *shipped* `content/` tree, deterministic under a fixed seed:
  - **The Tavern** — full game day: innkeeper routine flips with day phases (bar → sweep → bed),
    needs-driven patrons complete activity loops, Charisma pricing and night closing at the bar,
    a look-away meta-sensor interrupts dialogue, a patron comments on the rain; golden transcript.
  - **The Dungeon** — three GOAP soldiers flank via exclusive attack-node reservation (the
    F.E.A.R. Part 7 assert), rat-tier critters never touch the planner (counter-audited),
    kills roll loot tables, a poison trap ticks, an HTN boss falls back from ranged to melee.
  - **The Quest-Giver** — `quest_wolves` end-to-end (Yarn accept → event-bus counters → report →
    reward) across a mid-quest save/load.
- `AiRuntime.PlannerInvocations(/ByAgent)` perf counters; demo `perf` command.
- Demo host scene selection: `--scene tavern|dungeon|quest`.
- `docs/llm-guide.md` (condensed authoring guide for model contexts) and `docs/architecture.md`
  (layer map, hosting seams, extension walkthroughs).

### M6 — LLM & modding integration
- Blueprint inheritance (`inherits` + `$append`/`$remove`), content packs (`pack.json` overlays),
  `lattice manifest` exporter, matured schemas with CI drift gate, path-string UI bindings,
  rat-problem/GOAP-reachability validation warnings.

### M5 — World simulation
- Persisted deterministic clock/calendar (`Time.*` events, formula identifiers), day phases with
  `is_<phase>` flags, Markov weather holding sensor-scaling flags, season registry-redirect
  overlays, deterministic grid A* with per-profile state-dependent costs and node reservation.

### M4 — The AI suite
- Condition-bitmask world model with profile-calibrated sensors; five brain tiers over one task
  vocabulary (FSM, Half-Life schedules, reactive behavior trees + needs/utility, GOAP with
  budgeted A*/cost-profile personalities/reservation flanking, HTN with traced decomposition);
  group agents with role slots and staleness blackboards; the Collective; meta player awareness.

### M1–M3 — Foundation, RPG systems, narrative
- Def registry with link pass, event bus, lifecycle boot, NCalc formulas with dice, PCG32
  determinism, world-delta saves, hot reload; stats/modifiers/status effects, inventory/equip,
  loot, trade; Yarn + JSON dialogue over the blackboard, event-driven quests, smart objects.

### Known gaps (tracked in `plan/07-demos-and-polish.md`)
- Godot host sample (`samples/Lattice.Godot`) — the engine-seam proof in a real engine.
- Herd showcase demo scene (the M4d coordination assertions live in `Lattice.Ai.Tests.HerdTests`).
- Replay log, NuGet packaging, license decision.
