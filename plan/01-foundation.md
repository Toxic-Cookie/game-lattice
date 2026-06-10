# Plan 01 — Foundation: Scaffolding (M0) & Core Engine (M1)

> Implements concept **Phase 1: Core Engine & Data Plumbing**.
> The foundation that lets the game run as an *interpreter*: C# executes, JSON declares.

---

## M0 — Project Scaffolding

### Tasks
- [ ] Restructure solution per overview §3.2: `src/Lattice.Core`, `src/Lattice.Tooling`, `samples/Lattice.Demo`, `tests/Lattice.Core.Tests` (other projects added in their own milestones). Delete `Class1.cs`; retire `game_lattice` namespace for `Lattice.*`.
- [ ] `Directory.Build.props`: `Nullable`, `ImplicitUsings`, `LangVersion latest`, `TreatWarningsAsErrors`, analyzers; core libs target `netstandard2.1` (single TFM) with PolySharp polyfills for modern syntax; executables (Demo, Tooling, tests) target `net10.0`.
- [ ] Pin dependencies: `NCalc` (or `NCalcSync`/maintained fork — evaluate at implementation time), `System.Text.Json`, `xunit`, `YarnSpinner` (deferred to M3 but version-scouted now).
- [ ] Host Adapter Layer interfaces (overview §3.3): `ILatticeHost`, `IContentSource`, `INavigationService`, `IAnimationService`, `IPhysicsQueryService` + standalone/console implementations (stubs where needed).
- [ ] `Lattice.Demo` console app: loads a content directory, ticks the sim, REPL-style commands (`spawn`, `inspect`, `tick N`, `save`, `load`). This is the workbench for every later milestone.
- [ ] CI (GitHub Actions or local script): build + test + (later) content validation.

### Acceptance
- `dotnet build` and `dotnet test` green; demo app boots, loads an empty content dir, ticks.

---

## M1 — Core Engine & Data Plumbing

### 1. Data-to-Object Registry **[core]**

The central hub mapping JSON ID strings → loaded definition objects.

**Design**
- `DefRegistry`: `T Get<T>(string id)`, `bool TryGet<T>(...)`, `IEnumerable<T> All<T>()`, plus *unresolved-reference tracking* for the validation suite.
- IDs are namespaced by convention: `item_*`, `npc_*`, `quest_*`, `spell_*`, `schedule_*`, `goal_*`… The convention is data, not code: a `def-types.json` manifest maps prefix → def kind → schema (feeds plan 06).
- Loading pipeline: `IContentSource` → JSON parse → polymorphic def deserialization (`"$type"`-style discriminator on effect/condition/task payloads) → registry insert → **link pass** (resolve cross-references, report dangling IDs).
- Defs are immutable after the link pass. Mutation happens only on instances.

**JSON contract sketch**
```json
{
  "id": "item_iron_sword",
  "type": "item",
  "name": "Iron Sword",
  "slot": "MainHand",
  "useActions": [ { "type": "DealDamage", "formula": "Str * 2 + 4" } ]
}
```

- [ ] `DefRegistry` + loader + link pass with dangling-ID report
- [ ] Polymorphic payload deserialization (`EffectDef`, `ConditionDef` discriminated unions)
- [ ] Per-def-kind `.schema.json` emitted from C# model (groundwork for plan 06)

### 2. Event Bus **[core]**

String-based pub/sub: `Publish("PlayerKilledWolf", payload)`.

**Design**
- Global bus + *scoped* buses (per scene, per group) sharing one implementation; scoped buses can bubble to global.
- Payload: `EventPayload : IReadOnlyDictionary<string, object?>` — string-keyed so JSON-defined triggers can match on payload fields without typed contracts.
- Subscriptions support exact topic and wildcard prefix (`"Player*"`). Deferred dispatch queue drained at a fixed point in the tick (no re-entrant cascades mid-system).
- This bus is the spine of: quest progression (plan 03), schedule interrupts (plan 04), world triggers (plan 05), meta-awareness (plan 04 §8).

- [ ] `EventBus` with scopes, wildcards, deferred dispatch, subscription disposal
- [ ] Trace mode: ring buffer of last N events for the debug console (`events` command in demo)

### 3. Game Life Cycle Manager **[core]**

JSON-defined initialization: initial scene, starting inventory, global flags.

```json
{
  "id": "lifecycle_default",
  "initialScene": "scene_tavern",
  "startingInventory": [ { "item": "item_torch", "count": 2 } ],
  "globalFlags": { "tutorial_done": false }
}
```

- [ ] `GameSession`: `Boot(lifecycleId)` → load defs → create world → apply lifecycle → start tick loop
- [ ] Ordered system registration (`ISimSystem.Tick(dt)`) — explicit, deterministic system order

### 4. Formula Expression Parser **[core]**

- Wrap NCalc behind `IFormulaEngine` (`float Eval(string formula, IFormulaContext ctx)`); never expose NCalc types outside `Lattice.Core` (swappable, testable).
- `IFormulaContext` resolves bare identifiers (`Str`, `Level`) against a stat/blackboard scope chain: *instance stats → owner → global blackboard*.
- **Compiled-expression cache** keyed by formula string (formulas appear in hot paths: damage, loot, utility).
- Dice extension: `1d10+5` either via NCalc custom function `dice(1,10)` or a pre-pass that rewrites dice notation; deterministic under seeded RNG.
- Validation hook: `bool TryParse(formula, out errors)` used by the validation suite to pre-flight every formula string in content.

- [ ] `IFormulaEngine` + NCalc adapter + cache + dice support + seeded RNG
- [ ] Context scope chain; unknown-identifier errors name the formula's owning def ID

### 5. State Persistence — World Delta **[core]**

Serialize only what changed from base JSON.

**Design**
- Three layers: **defs** (immutable, from content), **instances** (runtime entities referencing defs), **delta** (save file).
- Save = `{ instances: [spawned/destroyed/moved + per-instance mutable state], globalBlackboard: {...}, clock: {...}, rngState }`. Def-derived values are never saved; on load, defs come from content and the delta is replayed on top.
- Version field + per-system migration hooks from day one (cheap now, painful later).
- AI runtime state (current plan, schedule index) is **not** saved in v1 — brains replan on load (research-sanctioned: plans are cheap and validated; saving them risks staleness). Persist only world-observable facts.

- [ ] `ISaveable` per system; `SaveManager.Capture()` / `Restore()`; JSON save format with version
- [ ] Determinism test: save → load → tick K ≡ tick K without save/load (modulo replan timing)

### 6. Hot-Reloading Watcher **[core]**

- `IContentSource.Changed` → debounce → re-parse changed file(s) → swap defs in registry → publish `Content.Reloaded {defIds}` on the event bus.
- Instances hold def *IDs*, not object references (or re-resolve via registry indirection), so a reload is visible immediately.
- Failed parse/validation ⇒ keep old defs, log error — a broken save while editing JSON must never kill the session (key authoring-loop requirement for LLM/human iteration).

- [ ] Debounced watcher, atomic def swap, reload event, parse-failure resilience
- [ ] Demo: edit `item_iron_sword` damage formula while running; next attack uses new value

### M1 Acceptance
- Demo: boot from lifecycle JSON, spawn entities from defs, publish/observe events, evaluate formulas with stats, save → quit → load → identical state, hot-edit a def live.
- Validation groundwork exists: dangling-ID report + formula pre-flight + emitted schemas.

### Stretch **[stretch]**
- Content packs/load order (mod layering: later packs override earlier IDs) — design hooks now (registry already supports replace), implement in plan 06.
