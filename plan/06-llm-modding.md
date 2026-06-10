# Plan 06 — LLM & Modding Integration (M6)

> Implements concept **Phase 6**: the meta layer that makes the framework writable by LLMs and modders. **Most of the raw material already exists by M6** because every phase shipped schemas, manifest entries, and validation rules with its systems (overview §4 reordering). M6 assembles, polishes, and exposes them as tools.

Depends on: M1+ metadata discipline. Deliverable shape: `Lattice.Tooling` CLI (`lattice <command>`), runnable standalone and in CI.

---

## 1. System Manifest Exporter **[core]**

The text "dictionary" of the game an LLM reads before writing content.

- `lattice manifest <contentDir> -o manifest.md` emits, grouped by def kind:
  - every registered ID + name + one-line description (description fields required by schema — validation enforces),
  - the **primitive vocabularies**: every effect/condition/task/steering primitive with arg signatures and a usage example (sourced from the `[PrimitiveDoc]` metadata each executor registers — plan 02 §3),
  - stat/condition-flag/slot/event-topic catalogs,
  - formula identifier scope documentation.
- Output is deliberately markdown (token-efficient, LLM-native). A `--json` mode emits the same data structured for tooling.

- [ ] Manifest generator + primitive-doc metadata attribute + CI artifact

## 2. JSON Schema Generator **[core]**

- `lattice schemas -o schemas/` — `.schema.json` per def kind, generated from the C# def models (single source of truth; schemas are never hand-maintained). Discriminated unions for primitive payloads (`"type": "DealDamage"` → its arg schema).
- `$schema` headers in content files → IDE/LLM IntelliSense for free.
- Cross-file ID references annotated with custom keyword (`"x-lattice-ref": "item"`) — drives both editor tooling and the validator.

- [ ] Schema emission from models + union discriminators + ref annotations + content `$schema` headers

## 3. Validation Suite **[core]**

The "pre-flight check" — already accumulated per phase; M6 unifies it as `lattice validate <contentDir>`:

| Rule class | Origin |
|---|---|
| Schema conformance | this plan §2 |
| Dangling ID references (items, quests, schedules, actions, …) | plan 01 link pass |
| Formula parse + unknown identifiers | plan 01 §4 |
| Derived-stat cycles, loot-table cycles | plan 02 |
| Yarn compilation, dialogue orphan nodes, quest reachability | plan 03 |
| Condition-bit budget (≤32), predicate-count budget per profile | plan 04 (ch07 anti-pattern 4) |
| Brain-tier mismatch warning (rat problem: ≤3 behaviors on GOAP/HTN) | plan 04 (ch07 §7.1) |
| GOAP reachability: every goal achievable from *some* start state via the action set (draw the predicate graph — ch03 checklist) | plan 04 |
| Unreachable nav positions for placed content | plan 05 |
| Blueprint cycle / unknown parent | this plan §4 |

- Output: human-readable + `--sarif`/`--json` for CI annotation. Severity tiers (error blocks load; warning logs).
- [ ] Unified runner + the rule registry + CI gate on `content/`

## 4. Prefab "Blueprinting" (Template Inheritance) **[core]**

- Any def may declare `"inherits": "npc_base_orc"`; loader resolves parent chain **before** schema validation. Merge semantics: objects deep-merge, scalars override, arrays replace by default with explicit `"$append"` / `"$remove"` operators (LLMs handle explicit operators better than implicit merge magic).
- Cycle detection + max depth; manifest lists blueprint hierarchies.
- Implemented in `Lattice.Core` loader (it changes load order), exercised heavily by demo content (`npc_elite_orc` = base + stat overrides + extra action subset).

- [ ] Inheritance resolution + merge operators + validation + manifest integration

## 5. Mod / Content Pack Layering **[core-lite]** *(promoted from plan 01 stretch)*

- `pack.json` per content dir: id, version, dependencies, load priority. Later packs override same-ID defs (registry overlay — same mechanism seasons use, plan 05 §4).
- `lattice validate` runs across the merged pack set.

- [ ] Pack manifest + ordered overlay loading + cross-pack validation

## 6. Dynamic UI Binding **[core-lite]**

Engine-agnostic data binding: a UI element needs only a path string.

- `BindingService`: `Subscribe("Player.stats.stat_hp", cb)` — path grammar over entities/stats/inventory/blackboard; backed by the existing change events (`Stat.Changed`, blackboard subscriptions), not polling.
- `UiBindingDef` **[stretch]**: JSON-declared HUD widgets (`{ "widget": "bar", "bind": "Player.stats.stat_hp", "max": "Player.stats.stat_hp.max" }`) interpreted by the host. Console demo renders them as text gauges — proving the contract.

- [ ] Path resolver + subscription bridge + console gauge renderer

---

## M6 Acceptance
- An LLM-shaped workflow test, end to end: given only `manifest.md` + `schemas/`, author a new spell, a new NPC (blueprint-inheriting), and a 2-step quest as JSON; `lattice validate` passes; content hot-loads into the running demo and functions — **no C# changes, no restart.** (This test is performed literally, with a model, and its prompt becomes part of plan 07's LLM guide.)
- CI runs validate + schema drift check (regenerate schemas, fail if uncommitted diff).

## Stretch **[stretch]**
- `lattice new <kind> <id>` scaffolding command (template + schema-valid skeleton)
- Manifest diffing between content versions (modder changelog aid)
