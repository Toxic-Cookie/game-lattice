# Plan 02 — RPG Logic: Stats & Rules Engine (M2)

> Implements concept **Phase 2**. Constraint: every system here must be definable in JSON without new C# classes for new effects. The C# vocabulary is a fixed set of *effect primitives*; content composes them.

Depends on: M1 (registry, formulas, events, persistence).

---

## 1. Attribute / Stat System **[core]**

**Design**
- `StatDef`: id, name, min/max/default — possibly formula-based (`"max": "10 + Level * 2"`).
- `StatSheet` (per entity instance): base values + computed current values; current = `clamp(base + Σ modifiers, min, max)`.
- Derived stats are formulas over other stats (`"carry_capacity": "Str * 10"`); dependency order resolved at link time, cycles rejected by validation.
- Changes publish `Stat.Changed {entity, stat, old, new}` (drives UI binding in plan 06 and AI conditions in plan 04).

```json
{ "id": "stat_hp", "type": "stat", "name": "Health", "min": 0, "max": "Con * 10", "default": "max" }
```

- [ ] `StatDef` + `StatSheet` + derived-stat resolution + change events
- [ ] Entity templates declare stat blocks by ID (`"stats": { "stat_hp": 30, "stat_str": 8 }`)

## 2. Modifier & Buff System **[core]**

**Design**
- `StatusEffectDef`: id, duration (or permanent), tick interval, stack policy (`stack | refresh | ignore | stack_capped`), and a list of *modifier primitives*:
  - `FlatModifier { stat, amount }` — additive
  - `PercentModifier { stat, percent }` — multiplicative
  - `PeriodicEffect { interval, effects[] }` — e.g. poison: every 1s apply `DealDamage`
  - `TagModifier { addTags[] }` — e.g. `"stunned"` (read by AI conditions)
- Application order is fixed and documented: flat → percent → clamp.
- `StatusEffectSystem.Tick` drives durations/intervals; expiry publishes `Status.Expired`.

```json
{
  "id": "status_poison",
  "type": "status",
  "duration": 10.0,
  "stacking": "refresh",
  "logic": [ { "type": "PeriodicEffect", "interval": 1.0,
               "effects": [ { "type": "DealDamage", "formula": "5" } ] } ]
}
```

- [ ] Modifier primitives + stacking policies + ordered application + tick lifecycle
- [ ] Effects integrate with formula context (`"formula": "CasterInt * 1.5"` — caster/target scopes)

## 3. Effect Primitive Vocabulary **[core]** *(cross-cutting — used by items, spells, quests, AI)*

This is the heart of the "interpreter" claim. One registry of `IEffectExecutor` implementations keyed by `"type"` string:

`DealDamage`, `Heal`, `ModifyStat`, `ApplyStatus`, `RemoveStatus`, `GiveItem`, `RemoveItem`, `PublishEvent`, `SetFlag`, `StartQuest` (plan 03), `PlayAnimation` (via adapter), `AreaDamage`, `SpawnEntity`, `Teleport`.

- Executors receive `(EffectContext { source, target, world, rng }, JsonElement args)`.
- New game content never adds executors; new *genres of capability* add exactly one executor + schema + manifest entry.
- Mirror vocabulary for **condition primitives** (`StatAtLeast`, `HasItem`, `HasTag`, `FlagEquals`, `FormulaTrue`, `All`/`Any`/`Not`) — shared by quests, dialogue, loot, AI preconditions.

- [ ] `IEffectExecutor` / `IConditionEvaluator` registries + the primitive set above
- [ ] Each primitive ships schema + validation rule + manifest description (plan 06 consumes)

## 4. Inventory & Item System **[core]**

- `ItemDef`: id, name, tags, stack size, slot (`Head|Chest|MainHand|OffHand|...` — slot list itself is data: `slots.json`), `useActions` (effect list), `equipEffects` (modifiers while equipped).
- `Inventory` instance: slots + bag; equip/unequip applies/removes `equipEffects` as a status-like modifier source.
- Events: `Item.Acquired`, `Item.Used`, `Item.Equipped` (quest triggers, AI smell-stimuli later).

- [ ] Inventory container + equip pipeline + use-action dispatch through effect primitives
- [ ] Slot definitions in data; validation rejects equipping to undeclared slots

## 5. Loot & Drop Tables **[core]**

- `LootTableDef`: weighted entries `{ item | tableRef | nothing, weight, amount: "1d10+5", conditions[] }`.
- Tables nest by reference (`tableRef`) → shared sub-tables; cycle detection in validation.
- Rolls use the seeded RNG (determinism tests) and the formula engine for amounts.

```json
{ "id": "loot_wolf", "type": "loot",
  "entries": [
    { "item": "item_gold", "weight": 50, "amount": "1d10+5" },
    { "item": "item_wolf_pelt", "weight": 30 },
    { "tableRef": "loot_rare_global", "weight": 2 } ] }
```

- [ ] Weighted roll + nesting + conditional entries + dice amounts

## 6. Economy / Trade Manager **[core-lite]**

- `ShopDef`: stock entries (item or loot-table restock), buy/sell price formulas — `"buyPrice": "BasePrice * (2.0 - Charisma * 0.01)"`.
- `TradeSession`: validates both sides (inventory space, gold), executes via effect primitives, publishes `Trade.Completed`.
- Restock hooks onto the time system (plan 05) — define the event contract now (`Time.DayStarted`), implement restock when M5 lands.

- [ ] ShopDef + price formulas + trade transaction + restock-on-event

---

## M2 Acceptance
- Demo scenario (headless): spawn player + wolf from templates; player uses `item_iron_sword` (formula damage), wolf applies `status_poison`; wolf dies → `loot_wolf` rolls; player sells pelt at a shop with Charisma-priced rates. **All content JSON, zero scenario-specific C#.**
- Determinism test covers loot rolls; validation catches: unknown stat in formula, dangling item ID, loot-table cycle, bad slot.

## Stretch **[stretch]**
- Crafting (recipes = conditions + effects — falls out of the primitive vocabulary almost free)
- Durability, item affixes (modifier lists on item instances)
