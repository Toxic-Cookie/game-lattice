# Game Lattice — LLM Authoring Guide

You are authoring content for Game Lattice, a data-driven RPG framework. The C# engine is a
**fixed interpreter**: it executes a closed vocabulary of effect/condition/task primitives and
never changes per content. Everything you write is JSON (plus Yarn dialogue). If your content
needs new C#, you are doing it wrong — recompose the existing primitives instead.

Pair this guide with two generated references:
- **`docs/manifest.md`** — every def currently registered, all primitive vocabularies with
  argument docs, the formula identifier scope, and event topics. Read it before authoring;
  reference existing ids from it.
- **`schemas/*.schema.json`** — one JSON Schema per def kind; `lattice.schema.json` validates a
  whole file.

## The model

- A **def** is a JSON object with `"id"` and `"type"` (the def kind). Files hold one def, an
  array of defs, or `{ "$schema": "...", "defs": [...] }`.
- **Ids** are `snake_case` with a kind prefix: `item_ale`, `entity_wolf`, `quest_wolves`,
  `schedule_patrol`, `profile_guard` (kind `agent`), `so_chest` (kind `smartobject`),
  `bt_patron`, `htn_boss`, `loot_wolf`, `status_poison`.
- **Cross-references are strings** (`"lootTable": "loot_wolf"`); a link pass validates every
  reference at load.
- **Formulas are strings** evaluated at runtime: `"Str * 2 + 4"`, `"2d6 + 4"` (dice),
  `"CAN_SEE_ENEMY * 50"` (conditions read as 1/0). Identifiers come from stat *keys* (`Str`,
  `HP`, `Charisma` — not the `stat_*` ids), need keys, condition names, global flags, and the
  clock (`Hour`, `Day`, `Season`). Rule of thumb: **def ids in structural fields, keys inside
  formulas**.
- **Entities** spawn from `entity` templates; **blueprints** compose them:
  `"inherits": "entity_player"` deep-merges, scalars override, arrays replace unless you patch
  with `{ "$append": [...] }` / `{ "$remove": [...] }`.
- A running session **hot-reloads** changed content files; saves store world deltas, so defs
  stay the single source of truth.

## Worked examples (all lifted from shipped `content/`)

**Item with behavior** — effects compose; no item class exists:
```json
{ "id": "item_healing_potion", "type": "item", "name": "Healing Potion",
  "tags": ["consumable"], "basePrice": 12, "consumeOnUse": true,
  "useActions": [ { "type": "Heal", "formula": "10" },
                  { "type": "RemoveStatus", "status": "status_poison" } ] }
```

**Blueprint NPC** — a variant is a diff, not a copy:
```json
{ "id": "entity_delver", "type": "entity", "inherits": "entity_player",
  "name": "Delver", "tags": { "$append": ["intruder"] }, "stats": { "stat_con": 20 } }
```

**Status effect** — periodic logic plus a tag while held:
```json
{ "id": "status_poison", "type": "status", "duration": 6.0, "stacking": "refresh",
  "logic": [ { "type": "PeriodicEffect", "interval": 1.0,
               "effects": [ { "type": "DealDamage", "formula": "2" } ] },
             { "type": "TagModifier", "addTags": ["poisoned"] } ] }
```

**Quest** — counters are event-driven; completion is a condition checked per tick:
```json
{ "id": "quest_wolves", "type": "quest", "name": "Wolves at the Door",
  "steps": [
    { "id": "cull", "description": "Cull 3 wolves",
      "count": { "counter": "wolves_killed", "on": "Entity.Died",
                 "where": { "defId": "entity_wolf" } },
      "complete": { "type": "FormulaTrue", "formula": "wolves_killed >= 3" } },
    { "id": "report", "description": "Report back",
      "complete": { "type": "FlagEquals", "flag": "reported_to_innkeeper", "value": true },
      "onComplete": [ { "type": "GiveItem", "item": "item_gold", "amount": "25" } ] } ],
  "onComplete": [ { "type": "PublishEvent", "event": "Quest.WolvesDone" } ] }
```

**Yarn dialogue** — `$vars` are global flags; prefer the sugar commands (`<<flag k v>>`,
`<<give item n>>`, `<<start_quest id>>`) over raw JSON inside `<<run>>` (braces collide with
Yarn interpolation):
```yarn
title: Innkeeper
---
Innkeeper: Welcome to the Wolf's Rest. What can I do for you?
-> Heard you have a wolf problem.
    <<if quest_active("quest_wolves") and flag_number("wolves_killed") >= 3>>
        <<flag reported_to_innkeeper true>>
        Innkeeper: Here's the gold, as promised.
    <<else>>
        <<start_quest quest_wolves>>
    <<endif>>
===
```

**Smart object** — world verbs for players *and* a plannable fact for AI (`aiEffects` lets GOAP
treat the object as an action; `maxUsers: 1` makes reservation the coordination):
```json
{ "id": "so_attack_node", "type": "smartobject", "entity": "entity_attack_node",
  "maxUsers": 1, "animation": "aim", "aiEffects": { "in_attack_position": true } }
```

**Shop** — prices are formulas over the *customer*; opening hours are conditions over flags:
```json
{ "id": "shop_innkeeper", "type": "shop",
  "buyPriceFormula": "BasePrice * (2.0 - Charisma * 0.01)",
  "sellPriceFormula": "BasePrice * (0.4 + Charisma * 0.01)",
  "restockOn": "Time.DayStarted",
  "openWhen": [ { "type": "Not", "condition":
                  { "type": "FlagEquals", "flag": "is_night", "value": true } } ],
  "stock": [ { "item": "item_ale", "count": 12 } ] }
```

## Authoring an NPC brain

An `agent` def binds entities to sensors, a condition catalog, and **one brain tier**. Pick the
cheapest tier that produces the behavior (see "what not to do"):

| `brain` | Use for | Behavior source |
|---|---|---|
| `fsm` | critters, ambient life | `fsmbrain` def (states, steering, transitions) |
| `schedules` | guards, villagers with routines | prioritized `schedule` defs gated by conditions |
| `bt` | reactive NPCs with needs | `btree` def (+ `need`/`activity`/`utility` defs) |
| `goap` | tactical combatants | `goapgoal` + `goapaction` (+ `costprofile`) |
| `htn` | scripted-but-adaptive routines, bosses | `htncompound` (ordered methods → goapaction primitives) |

```json
{ "id": "profile_innkeeper", "type": "agent", "entities": ["entity_innkeeper"],
  "brain": "schedules",
  "schedules": ["schedule_innkeeper_scold", "schedule_innkeeper_bed",
                "schedule_innkeeper_sweep", "schedule_tend_bar"],
  "conditions": "conditions_default",
  "metaSensors": ["metasensor_lookaway"],
  "flagConditions": { "IS_NIGHT": "is_night", "IS_DUSK": "is_dusk" },
  "hostileTags": ["wolf"],
  "sensors": [ { "kind": "visual", "range": 8, "fov": 360, "sensitivity": 0.7 },
               { "kind": "auditory", "range": 8 } ],
  "walkSpeed": 1.6, "runSpeed": 3.0, "alertDecaySeconds": 3.0 }
```

A schedule from that routine — atomic tasks, condition-gated, highest eligible priority wins:
```json
{ "id": "schedule_innkeeper_bed", "type": "schedule", "priority": 30,
  "metaStates": ["Idle"], "require": ["IS_NIGHT"],
  "interrupt": ["CAN_SEE_ENEMY", "THREAT_KNOWN", "HEAR_SOUND", "DAMAGED"],
  "tasks": [ { "task": "MoveTo", "target": [6, 0, 6], "speed": "walk" },
             { "task": "PlayAnimation", "anim": "sleep" },
             { "task": "Wait", "seconds": 3.0 },
             { "task": "SelectNewSchedule" } ] }
```

GOAP personality is data: goals score relevance (`"priority": "CAN_SEE_ENEMY * 50"`; 0 means
irrelevant), actions declare predicate `preconditions`/`effects`, and a `costprofile` makes the
same action set brave or cowardly by re-costing one action. HTN method **order is the
doctrine** — first method whose preconditions hold decomposes:
```json
{ "id": "htn_boss", "type": "htncompound", "methods": [
    { "name": "ranged", "preconditions": { "CAN_SEE_ENEMY": true, "has_arrows": true },
      "subtasks": ["action_shoot_bow"] },
    { "name": "melee",  "preconditions": { "CAN_SEE_ENEMY": true },
      "subtasks": ["action_charge", "action_claw"] },
    { "name": "lurk", "subtasks": ["action_lurk"] } ] }
```

Condition names (`CAN_SEE_ENEMY`, `IS_NIGHT`, ...) must exist in the agent's `conditions`
catalog (32 max). World state reaches brains through data bridges: `flagConditions` turns
truthy global flags into condition bits; `metasensor` defs turn player behavior patterns on the
event bus into conditions; weather flags like `sense_auditory_mult` rescale sensors.

## The loop

1. Read `docs/manifest.md` for the current vocabulary; never invent primitive or def ids.
2. Write JSON/Yarn into `content/` (or a mod pack: a directory with `pack.json` overlays the
   base content by priority).
3. Validate: `dotnet run --project src/Lattice.Tooling -- validate content` — fix every error
   *and* warning; the validators encode the design rules below.
4. A running session hot-reloads the file; or run `dotnet run --project samples/Lattice.Demo`
   and inspect with `defs`, `agent <id>`, `dump <id>`, `trace <id>`, `quests`, `events`.

## What NOT to do

- **No predicate bloat.** GOAP/HTN predicates are a handful of booleans per role
  (`weapon_loaded`, `in_attack_position`). If you need ten, you want two actions, not ten
  predicates. The condition catalog is a hard 32-bit budget.
- **No brain-tier mismatch (the rat problem).** A rat gets a 2-state `fsm`, never a planner.
  Planner-tier brains are for agents whose failures the player will actually watch.
- **No hive minds.** Agents never read each other's beliefs; coordination flows only through
  group blackboards (which have per-key staleness) and smart-object reservation. An unwitnessed
  event must stay unwitnessed.
- **No instantaneous conditions in long-lived gates.** Sensor bits flicker at range boundaries;
  gate flee/calm behavior on `{"type":"AgentMeta","is":"Alert"}`, which decays smoothly.
- **No polling.** UI reads binding paths (`Player.stats.stat_hp`, `flags.weather`) that push on
  change; reactions to world changes subscribe to events (`Entity.Died`, `Weather.Changed`,
  `Time.PhaseChanged`) or read flags in conditions.
- **No new primitives via prose.** If a behavior truly has no primitive, say so instead of
  inventing JSON the interpreter will reject.

## End-to-end example (the M6 acceptance mod)

This file — shaped exactly like expected LLM output — hot-loads into a running session and
works immediately: the blueprint NPC spawns merged, the quest runs, the bomb explodes once.
```json
{ "$schema": "../schemas/lattice.schema.json",
  "defs": [
    { "id": "item_frost_bomb", "type": "item", "name": "Frost Bomb",
      "basePrice": 30, "consumeOnUse": true,
      "useActions": [ { "type": "AreaDamage", "formula": "3d6 + 4", "radius": 4 },
                      { "type": "SetFlag", "flag": "frost_used", "value": true } ] },
    { "id": "entity_guard_captain", "type": "entity", "inherits": "entity_guard",
      "name": "Guard Captain", "stats": { "stat_con": 9 },
      "tags": { "$append": ["captain"] } },
    { "id": "quest_initiation", "type": "quest", "name": "Initiation",
      "steps": [
        { "id": "train", "description": "Finish training",
          "complete": { "type": "FlagEquals", "flag": "training_done", "value": true } },
        { "id": "report", "description": "Report to the captain",
          "complete": { "type": "FlagEquals", "flag": "reported_in", "value": true },
          "onComplete": [ { "type": "GiveItem", "item": "item_frost_bomb", "amount": "1" } ] } ] } ] }
```
