# Game Lattice — Architecture

For framework developers. Content authors (human or LLM) should start with
[`llm-guide.md`](llm-guide.md) instead — nothing in this document is needed to write content,
which is the point of the design.

## The premise

C# provides a **fixed interpreter**; JSON provides the soul. Every gameplay noun (item, quest,
brain, weather state) is a *def* interpreted by a closed vocabulary of effect / condition /
task primitives. Adding content never requires recompilation; adding a new *primitive* is a
deliberate, small C# event (see [Extension walkthroughs](#extension-walkthroughs)).

## Assembly map

```
Lattice.Core        registry, events, lifecycle, formulas, persistence, hosting seams   (netstandard2.1)
  └─ Lattice.Rpg        stats, statuses, effect/condition primitives, inventory, loot, trade, UI bindings
       └─ Lattice.Narrative   Yarn + JSON dialogue, quests, smart objects & reservation
            └─ Lattice.Ai          sensors, condition bitmask, 5 brain tiers, groups, collective, meta-sensors
  └─ Lattice.World      clock/calendar, day phases, Markov weather, season overlays, grid A*  (refs Core+Rpg)
Lattice.Tooling     `lattice` CLI: validate | manifest | schemas                        (net10.0)
samples/Lattice.Demo    headless console host / workbench
tests/Lattice.Demos.Tests   the three demo scenes run as simulation tests over shipped content/
```

Modules attach in dependency order onto a `GameSession`, then content loads once:

```csharp
var session  = GameSession.Create(services, LatticeWorld.AddDefTypes(LatticeAi.CreateDefTypes()));
var rpg       = LatticeRpg.Attach(session);
var narrative = LatticeNarrative.Attach(session, rpg);
var ai        = LatticeAi.Attach(session, rpg, narrative);
var world     = LatticeWorld.Attach(session, rpg);   // composition is additive
session.LoadContent();
session.Boot("lifecycle_tavern");                     // a lifecycle def is a bootable scene
```

## Hosting seams

Engines integrate by implementing five interfaces in `Lattice.Core.Hosting` and handing them to
`HostServices` — the `Lattice.*` libraries never reference an engine:

| Seam | Standalone/test impl | Engine impl (e.g. Godot) |
|---|---|---|
| `ILatticeHost` | `StandaloneHost` (seeded RNG, logger, wall clock) | engine clock + log |
| `IContentSource` | `DirectoryContentSource` (+ file-watch hot reload) | `res://` packs |
| `INavigationService` | `GridNavigationService` (deterministic A*) / straight-line | engine navmesh |
| `IAnimationService` | `TimedStubAnimationService` | AnimationPlayer/Animator |
| `IPhysicsQueryService` | `PermissivePhysicsQueryService` (always-true LOS) | raycasts, overlap queries |

Everything above the seams is deterministic under a fixed seed — which is what lets the demo
scenes run headless in CI as simulation tests.

## Data backbone (Core)

- **DefRegistry** — id → typed def. Load order: parse files (single def / array / `defs`
  wrapper) → **blueprint pass** (`inherits` deep-merge with `$append`/`$remove` array patches,
  cycle/kind checks) → **link pass** (every `DefReference` resolves or reports). Content packs
  (`pack.json` directories) overlay base content in priority/dependency order.
  `SetRedirects` lets a *season* swap what an id resolves to at typed-lookup time — overlays as
  data.
- **EventBus** — string topics, queued during a tick, dispatched at safe points
  (`DispatchPending`); keeps a trace ring the `events` command and quest counters read. Events
  are the only cross-system fan-out: death → loot, death → quest counters, weather → senses.
- **Formulas** — NCalc-backed; identifiers resolve through an `IFormulaContext` chain (entity
  stat keys → need keys → condition names as 1/0 → global flags → clock identifiers). Dice
  (`2d6+4`) route through the seeded RNG.
- **Blackboard** — global flags (`Session.Flags`); also the store behind Yarn `$variables` and
  quest counters. Group agents get scoped blackboards with per-key staleness.
- **Persistence** — saves capture the *delta* against defs (spawned/despawned/changed
  entities, flags, plus module save sections: `rpg`, `narrative`, `world`). Agent runtime state
  is deliberately not saved: brains re-perceive and re-decide after load.

## The AI stack (per agent, per tick)

```
perception   SensorPipeline: profile-calibrated sensors (visual FOV/LOS, hearing, smell)
                · weather flags rescale ranges (sense_auditory_mult …)
                · flagConditions bridge world flags → condition bits
                · meta-sensors turn player behavior patterns on the bus → condition bits
world model  ConditionSet (32-bit catalog mask) over WorldState beliefs (scalars + positions)
decision     one brain tier: fsm | schedules | bt | goap | htn   (ThinkInterval decouples cadence)
execution    shared task primitives; GOAP/HTN plans run a GoTo → Animate → UseSmartObject loop
                · smart-object steps reserve at activation; denial → replan → flanking
coordination group agents (role slots, alert ladder, scoped blackboard), the Collective
                (spawn budgets, passport recycling) — never direct belief sharing
```

The brain tiers share the task/condition vocabulary, so content can move an NPC up or down a
tier without rewriting its verbs. `AiRuntime.PlannerInvocations(ByAgent)` counts every GOAP
plan / HTN decomposition — the dungeon test asserts the rat tier never appears in it.

## World simulation

`WorldRuntime` advances a persisted game clock (`time` def) publishing `Time.*` events and
writing `Hour`/`Day`/`Season`/`is_<phase>` flags; day phases set ambient light; weather is a
Markov chain on the hour whose states *hold flags* while active (rain halving hearing is one
number in data) and run tag-scoped boundary effects; seasons bias weather and overlay the
registry via redirects. `GridNavigationService` interprets `navgrid` defs (ASCII rows + legend)
with per-profile, per-behavior-state costs (`navprofile`) and node reservation — tall grass
impassable on patrol but crossable when alert is pure data.

## Observability

Every decision is reconstructable from the demo shell (`samples/Lattice.Demo`):
`agent` (brain + trace) · `senses` (beliefs + conditions) · `bt` (live tree) · `utility`
(scoreboard) · `needs` · `dump` (GOAP state/goals/plan/candidates) · `trace` (HTN
decomposition) · `groups`/`bb`/`roles` · `path` · `time` · `events` · `quests` · `hud`/`bind`.

A quest playthrough as the event bus tells it (the Quest-Giver demo):

```
[12] Quest.Started {quest=quest_wolves}
[57] Stat.Changed {instanceId=entity_wolf#3, stat=HP, old=15, new=0}
[57] Entity.Died {instanceId=entity_wolf#3, defId=entity_wolf, killerId=entity_player#1}
[57] Loot.Dropped {instanceId=entity_wolf#3, killerId=entity_player#1, items=item_gold:9}
 ⋮    (two more wolves)
[201] Quest.StepCompleted {quest=quest_wolves, step=cull}
[412] Quest.StepCompleted {quest=quest_wolves, step=report}
[412] Quest.Completed {quest=quest_wolves}
[412] Quest.WolvesDone {}
```

## Extension walkthroughs

**Adding an effect primitive** (the only common reason to touch C#):
1. Implement `IEffectExecutor` — `Type` (the JSON `"type"` discriminator), `Execute(ctx, args)`,
   `Validate(args, v)` — and decorate with `[PrimitiveDoc(...)]` so the manifest documents it.
2. Register it where sessions are built (module attach does this for built-ins) **and** in all
   three `src/Lattice.Tooling/Program.cs` registration sites (validate / manifest / schemas),
   which also folds it into the schema union vocabulary.
3. Add a validation test asserting `Validate()` rejects malformed args, and regenerate
   `schemas/` + `docs/manifest.md` (CI diffs schemas and fails on drift).

Condition primitives (`IConditionEvaluator`) and task primitives (`ITaskExecutor`) follow the
same three steps against their registries.

**Adding a def kind**: subclass `Def` (override `GetReferences()` for link checking), register
in the module's `CreateDefTypes()` (`types.Register<MyDef>("mykind")`), validate in the
module's content validator, regenerate schemas/manifest.

**Adding content**: no C#. See [`llm-guide.md`](llm-guide.md).

## The demos are the spec

`tests/Lattice.Demos.Tests` boots the shipped `content/` tree (not fixtures) and plays the
three scenes headless: the **Tavern** (a full game day: routines flip with day phases, patrons
run need loops, Charisma pricing, the look-away meta-sensor, rain commentary — with a golden
transcript), the **Dungeon** (flanking emerging from reservation, the rat-tier planner audit,
loot-on-kill, the poison trap, HTN method fallback), and the **Quest-Giver** (the full quest
chain with a mid-quest save/load). When in doubt about intended behavior, read those tests.
