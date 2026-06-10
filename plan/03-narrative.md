# Plan 03 — Narrative & Interaction (M3)

> Implements concept **Phase 3**: the bridge that lets an LLM write stories and quests.

Depends on: M1 (events, registry, formulas), M2 (conditions/effects vocabulary, inventory for rewards).

---

## 1. Global Blackboard (World State) **[core]** — *build first*

The dictionary of every choice the player has made; the substrate for quest conditions, dialogue branching, and AI group knowledge (plan 04 reuses this type).

**Design** (per research ch04 §4.4 — this is the same Blackboard the AI suite needs, so it lives in `Lattice.Core`):
- `Blackboard`: `Write(key, value)`, `Read(key, default)`, `ReadWithAge(key)`, `IsStale(key, maxAge)`, `Subscribe(key, cb)`, timestamped writes, pub/sub notification.
- One **global** instance (world flags: `"met_innkeeper": true`, `"wolves_killed": 3`); scoped instances created per AI group in plan 04.
- Persisted in the world delta (plan 01 §5). Exposed to formulas (`FlagEquals`, counters in conditions).

- [ ] `Blackboard` with timestamps, staleness, subscription; global instance wired into save/load and the formula context

## 2. YarnSpinner Integration **[core]**

Per the concept doc: don't reinvent dialogue — integrate [YarnSpinner](https://github.com/YarnSpinnerTool/YarnSpinner).

**Design**
- `Lattice.Narrative` wraps the Yarn **Dialogue** runtime (core lib is engine-agnostic, netstandard-compatible — verify pinned version at M0).
- Bridge both directions:
  - **Yarn → Lattice:** register Yarn *commands/functions* that call the effect/condition primitive registries — `<<give item_iron_sword 1>>`, `<<start_quest quest_wolves>>`, `lattice_flag("met_innkeeper")` in Yarn expressions. One generic command marshals to any effect primitive, so new primitives are automatically scriptable.
  - **Lattice → Yarn:** `IVariableStorage` implementation backed by the global blackboard — Yarn's `$variables` *are* world state; no second source of truth.
- `DialogueRunner` service: host-agnostic line/option presentation via `IDialogueView` adapter (console impl in demo).
- Yarn projects live in `content/dialogue/`; compiled at load; hot-reload supported like other content.

- [ ] Yarn runtime wrapper + blackboard-backed variable storage + primitive-bridge commands + `IDialogueView`
- [ ] Validation: compile all Yarn files in CI; unknown command/ID references reported

## 3. Branching Dialogue (JSON node trees) **[core-lite]**

The concept lists both Yarn *and* a JSON node-based tree. Decision: **Yarn is the primary dialogue system**; the JSON tree is a thin secondary format for machine-generated micro-dialogues (LLMs emit JSON more reliably than Yarn syntax).

- `DialogueTreeDef`: nodes `{ id, line, speaker, options: [{ text, conditions[], effects[], next }] }` reusing the shared condition/effect primitives (`if Gold > 100 → ShowNode(5)` is just a `FormulaTrue` condition).
- Runs through the same `DialogueRunner`/`IDialogueView` as Yarn — callers can't tell which backend served the conversation.

- [ ] `DialogueTreeDef` + interpreter over the shared runner; schema + validation (dangling `next`, unreachable nodes)

## 4. Quest Engine **[core]**

Step-based: each step = **Requirements** (conditions) + **Results** (actions) — exactly the shared primitive vocabulary.

**Design**
```json
{
  "id": "quest_wolves",
  "type": "quest",
  "name": "Wolves at the Door",
  "steps": [
    { "id": "kill_wolves",
      "description": "Cull 3 wolves",
      "complete": { "type": "CounterAtLeast", "counter": "wolves_killed", "value": 3 },
      "onComplete": [ { "type": "PublishEvent", "event": "Quest.StepDone" } ] },
    { "id": "report",
      "complete": { "type": "FlagEquals", "flag": "reported_to_innkeeper", "value": true },
      "onComplete": [ { "type": "GiveItem", "item": "item_gold", "amount": "25" } ] }
  ]
}
```
- `QuestLog` instance tracks per-quest step index + state (`Available|Active|Completed|Failed`).
- **Event-driven progression:** quest system subscribes to bus events declared in step triggers (`"increment": { "counter": "wolves_killed", "on": "Entity.Died", "where": { "tag": "wolf" } }`) — no polling. Counters live on the global blackboard.
- Branching/parallel steps **[stretch]**: v1 is linear step lists; the schema reserves `next`/`parallel` fields.

- [ ] QuestDef + QuestLog + event-driven counters/conditions + rewards via effects + persistence
- [ ] `quests` debug command: list active quests, step states, blocking conditions (why-not view)

## 5. Interaction Framework — Smart Objects **[core]**

"What happens when the player (or an AI) interacts with this object." Designed once, shared by player interaction *and* the AI suite (F.E.A.R. smart objects, research ch03 §3.5 — same concept, same def).

**Design**
- `SmartObjectDef`: id, `interactions: [{ verb: "OnInteract"|"OnUse"|..., conditions[], effects[] }]`, plus AI-facing fields used in plan 04: `approachPosition`, `animation`, `preconditions` (world-state predicates), `aiEffects` (world-state changes), `maxUsers`.
- Placed instances: `{ defId, position, stateOverrides }` in scene JSON.
- Occupancy/reservation API (`TryReserve/Release`) — the exclusion mechanism that later produces coordination (research ch01 §1.5).
- Player interaction = `Interact(entity, object, verb)` → conditions → effects (e.g. `OpenUI(Chest)` is a `PublishEvent` the host UI listens to).

- [ ] SmartObjectDef + scene placement + verb dispatch + reservation API
- [ ] AI-facing fields present in schema now (consumed in M4c)

---

## M3 Acceptance
- Demo: talk to innkeeper (Yarn), accept `quest_wolves`, kill wolves (events increment counter), report back (dialogue sets flag), receive reward; open a chest smart-object gated on a condition. Save/load mid-quest preserves all state. **Zero scenario C#.**
- Validation: Yarn compiles, quest references resolve, dialogue trees have no orphan nodes.

## Stretch **[stretch]**
- Quest branching/failure paths; timed quests (hooks on plan 05 time events)
- Localization pass-through (Yarn line IDs; JSON trees get string-table indirection)
