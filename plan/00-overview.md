# Game Lattice — Implementation Plan: Overview & Roadmap

> **Status:** Draft v1 (2026-06-10)
> **Inputs:** `research/game-lattice-concept.md`, `research/emergent-ai-guide/` (ch01–ch07), `research/case-studies/` (Half-Life, FSM theory, F.E.A.R., Horizon Zero Dawn)
> **Plan files:** this overview + `01`–`07` (one file per major phase). Each phase file contains design decisions, JSON contracts, task checklists, acceptance criteria, and research citations.

---

## 1. Vision

**Game Lattice** is an engine-agnostic, data-driven RPG framework where **C# provides the engine and JSON provides the soul**. The C# core is an *interpreter*: it knows how to execute a fixed vocabulary of primitive effects, conditions, tasks, and behaviors. All game content — items, spells, quests, NPC brains, weather, economies — is declared in JSON and never requires recompilation.

Two non-negotiable design axioms (from the concept doc):

1. **Strings over Types** — content references content by string ID (`"item_iron_sword"`), never by C# type. This is what makes the framework LLM-friendly: a language model can author valid content knowing only the ID dictionary and the JSON schemas.
2. **Declarative over Imperative** — content declares *what* (conditions, effects, costs, weights); the engine decides *how* and *when*.

The canonical test: adding a Fireball spell is a JSON entry composed of known effect primitives (`AreaDamage`, `ApplyStatus`), not a `Fireball.cs` class.

---

## 2. Architecture Principles (synthesized from research)

These recur across every case study and govern every design decision in phases 1–7:

| Principle | Source | Consequence for Lattice |
|---|---|---|
| **Emergence = independent agents + shared world state** | ch01, all case studies | Agents never reference other agents directly; they read/write world state, blackboards, and reserve shared resources |
| **No agent knows another exists** | F.E.A.R. Part 7, HZD Part 11 | Coordination is achieved via exclusion (node/object reservation) and blackboards with latency, never direct messaging between brains |
| **Constraints are coordinators** | Half-Life (32-bit conditions, unblendable tasks), HZD (role slots) | We deliberately keep the condition bitmask capped, tasks atomic, and role slots limited — these are features, not limitations |
| **Match technique complexity to agent complexity** | F.E.A.R. rat problem (ch07) | The AI suite is *tiered*: FSM for simple agents, BT for mid, GOAP/HTN for complex. JSON picks the brain type per agent profile |
| **Data-driven world interaction** | F.E.A.R. smart objects | World objects carry their own preconditions/effects/animations; AI discovers them at runtime |
| **The world model is a budget** | Half-Life conditions | Few, expressive predicates; abstraction over enumeration (ch07 anti-pattern 4) |
| **Plans are validated, not trusted** | F.E.A.R. 3 mechanisms | Simulate before execution, monitor continuously, re-check per action |
| **Leave room for incidental emergence** | HZD Stormbird | Debug/observability tooling is built early (ch07) so surprising behaviors can be found and amplified |

---

## 3. Technical Foundation

### 3.1 Target framework & engine-agnosticism

The current scaffold targets `net10.0`. That is fine for a standalone/console host and Godot 4, **but Unity cannot consume net10.0 assemblies**. Decision (confirmed 2026-06-10):

- **Core libraries target `netstandard2.1` only**, with `LangVersion latest` + [PolySharp](https://github.com/Sergio0694/PolySharp)-generated polyfills so modern C# syntax (records, `init`, `required`, pattern matching) compiles against the old TFM. One TFM keeps Unity (2021.2+) and Godot 4 both viable and avoids multi-target `#if` drift. Runtime-dependent features that polyfills cannot provide (e.g., default interface members are fine on ns2.1, but no `Span`-based BCL overloads beyond ns2.1's surface) are simply avoided in core.
- **Executables target `net10.0`**: `Lattice.Demo`, `Lattice.Tooling` (CLI), and all test projects.
- No engine types anywhere in core. Engine integration happens exclusively through the **Host Adapter Layer** (`ILatticeHost` and friends, §3.3).
- Dependencies must support netstandard2.0/2.1: **NCalc** (formulas), **YarnSpinner core** (dialogue), **System.Text.Json** (NuGet package works on netstandard2.0). All verified compatible; pin exact versions at Phase 0.

### 3.2 Solution restructure (Phase 0)

The existing `game-lattice.csproj` / `Class1.cs` / `game_lattice` namespace is placeholder scaffolding. Replace with:

```
game-lattice.slnx
├── src/
│   ├── Lattice.Core/          # registry, event bus, lifecycle, persistence, formulas, hot reload
│   ├── Lattice.Rpg/           # stats, modifiers, inventory, loot, economy
│   ├── Lattice.Narrative/     # quests, dialogue, Yarn integration, interactions
│   ├── Lattice.Ai/            # sensors, FSM/BT/GOAP/HTN/utility, blackboards, hierarchy
│   ├── Lattice.World/         # time, weather, seasons, navigation
│   └── Lattice.Tooling/       # manifest exporter, schema generator, validation suite (CLI)
├── samples/
│   ├── Lattice.Demo/          # console host + the demo scenes (data in /content)
│   └── Lattice.Godot/         # Godot 4 host sample (M7) — proof of the adapter seam
├── tests/
│   ├── Lattice.Core.Tests/    # one test project per src project (xunit)
│   └── ...
└── content/                   # shared demo JSON: defs, brains, quests, scenes
```

Namespace root: `Lattice.*` (PascalCase; drop the `game_lattice` snake_case). Project file conventions via `Directory.Build.props` (nullable enabled, implicit usings, analyzers, `TreatWarningsAsErrors`).

### 3.3 Host Adapter Layer (the engine-agnostic seam)

Everything physical is delegated to the host through interfaces; the framework itself is headless:

| Interface | Provides | Default implementation |
|---|---|---|
| `ILatticeHost` | clock, logging, RNG seed | `StandaloneHost` (console) |
| `INavigationService` | `FindPath`, node reservation, reachability | Built-in grid A* (Phase 5); engines swap in NavMesh |
| `IAnimationService` | play/query/interruptibility of named animations | No-op/timed stub for headless runs |
| `IPhysicsQueryService` | raycast (LOS), radius queries | Grid-based stub |
| `IContentSource` | JSON file enumeration + change notification | `DirectoryContentSource` (FileSystemWatcher) |

This is what lets the entire framework — including the AI suite and all three demo scenes — run and be tested in a plain console app, then dropped into Unity/Godot by implementing five interfaces.

### 3.4 Core runtime model

- **Defs vs. instances.** JSON files declare immutable *definitions* (`ItemDef`, `AgentProfileDef`, `ScheduleDef`...). Runtime *instances* reference defs by ID and hold mutable state. Save files persist only instance state + world delta (§ plan 01).
- **Deterministic tick.** The simulation advances on a fixed tick (`Tick(float dt)`), driven by the host. Tiered update frequencies for AI per ch06 §6.9.
- **Single-threaded simulation core** (v1). Determinism and debuggability beat parallelism for a framework whose selling point is inspectability. Revisit after profiling.

---

## 4. Roadmap & Milestones

Two cross-cutting changes to the concept doc's phase ordering (senior-dev review of the plan itself):

1. **Validation, schemas, and the ID manifest (concept Phase 6) are not a phase — they are a continuous practice.** Every system ships its `.schema.json`, validator rules, and manifest entries *in the same milestone* the system lands. Phase 6 then only assembles the LLM-facing tooling around already-existing metadata. Retrofitting validation onto five phases of unvalidated JSON would be far more expensive.
2. **Debug/observability tooling (research ch07) is pulled forward** into each AI sub-milestone (decision dumps, blackboard inspector, schedule traces). The research is unambiguous that GOAP/HTN are undebuggable without this.

| Milestone | Content | Plan file | Depends on |
|---|---|---|---|
| **M0** | Solution restructure, adapters, CI, test harness | `01` | — |
| **M1** | Core engine: registry, event bus, lifecycle, formulas, persistence, hot reload | `01` | M0 |
| **M2** | RPG logic: stats, modifiers, inventory, loot, economy | `02` | M1 |
| **M3** | Narrative: blackboard, quests, dialogue/Yarn, interactions (smart objects) | `03` | M1, M2 |
| **M4a** | AI: world model, sensors, FSM family, schedules (Half-Life pattern) | `04` | M1 |
| **M4b** | AI: behavior trees, utility functions | `04` | M4a |
| **M4c** | AI: GOAP (planner, execution FSM, smart-object actions, validation) | `04` | M4a, M3 (smart objects) |
| **M4d** | AI: HTN, blackboard groups, roles, collective; meta player awareness | `04` | M4c |
| **M5** | World: time, day/night, weather, seasons, grid navigation | `05` | M1 (nav needed by M4a for non-trivial demos — see note in `05`) |
| **M6** | LLM & modding: manifest exporter, schema generator CLI, validation CLI, blueprint inheritance, UI binding | `06` | M1+ (assembles per-phase metadata) |
| **M7** | Demos (Tavern, Dungeon, Quest-Giver), LLM authoring guide, polish | `07` | everything |
| **M8** | Content editor (Lattice.Studio): schema-driven forms + node graphs over the M6 tooling | `08` | M1, M6 (+ all def-kind milestones) |

Sequencing note: M5's time system and grid navigation are small and unblock M4 demos; in practice schedule M5-time right after M1 and M5-nav alongside M4a.

---

## 5. Testing & Verification Strategy

- **Unit tests per system** (xunit): planners get the heaviest coverage — GOAP/HTN planning is pure function `(state, defs) → plan`, ideal for table-driven tests reproducing the research examples (e.g., the cost-3 vs cost-5 A* example from ch03 §3.3).
- **Golden-file content tests**: every demo JSON file must pass the validation suite in CI; schemas are tested against valid + invalid fixtures.
- **Simulation tests**: headless scenario runner — load scene JSON, tick N times, assert on world state (e.g., "guard reaches Combat schedule within 60 ticks of player entering FOV"). This is how emergent-behavior regressions are caught.
- **Determinism test**: same seed + same content + same tick count ⇒ identical world-state hash.
- Every milestone's acceptance criteria (listed per plan file) must be demonstrable by a test or a demo scene command.

---

## 6. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| **Scope** — concept spans 7 phases × dozens of systems | Strict milestone gating; each phase file marks items **[core]** vs **[stretch]**; stretch items are cut first |
| **GOAP/HTN debugging difficulty** (ch03 §3.10, ch07) | Decision-dump and trace tooling built *with* the planners, not after |
| **Predicate explosion** makes planning intractable (ch07 anti-pattern 4) | Validation suite warns when world-state keys per agent exceed budget; favor computed/abstract predicates |
| **Engine-agnostic claim untested** | M7 (in scope): minimal Godot host sample proving the adapter seam; until then the console host is the proof |
| **NCalc/Yarn version drift on netstandard2.1** | Pin versions in M0; adapter-wrap both so they're swappable |
| **Hot reload corrupting live state** | Reload replaces *defs* only; instances re-resolve by ID; reload event lets systems re-validate (plan 01 §5) |
| **Rat problem — over-powered brains on simple agents** (ch07 §7.1) | Agent profiles choose brain tier in data; validation warns when a profile with ≤3 behaviors uses GOAP/HTN |

---

## 7. Glossary (project-wide vocabulary, from research 00-index)

**Def / Instance** · **World State** (string→value predicates) · **Condition bitmask** (≤32 fast flags) · **Task** (atomic behavior) · **Schedule** (condition-gated task sequence) · **Action** (precondition+effect+cost planning unit) · **Goal** (desired world state with dynamic priority) · **Method** (HTN decomposition option) · **Blackboard** (timestamped shared store) · **Smart Object** (world object carrying its own interaction data) · **Utility** (0–1 scored desirability) · **Agent Profile** (JSON: brain tier + action subset + sensor calibration) · **Group Agent** (non-physical herd coordinator) · **Collective** (ecosystem manager) · **Passport** (agent capability metadata for group matching).

---

## 8. Plan File Index

| File | Scope |
|---|---|
| `01-foundation.md` | M0 scaffolding + M1 core engine & data plumbing |
| `02-rpg-systems.md` | M2 stats, modifiers, inventory, loot, economy |
| `03-narrative.md` | M3 quests, dialogue, world blackboard, interactions |
| `04-ai-suite.md` | M4a–M4d the full AI brain suite |
| `05-world-simulation.md` | M5 time, weather, seasons, navigation |
| `06-llm-modding.md` | M6 manifest, schemas, validation, blueprints, UI binding |
| `07-demos-and-polish.md` | M7 demo scenes, LLM docs, debug tooling assembly |
| `08-content-editor.md` | M8 Lattice.Studio visual content editor (forms + node graphs) |
