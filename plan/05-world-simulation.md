# Plan 05 — World & Environment (M5)

> Implements concept **Phase 5**: immersion/simulation systems. Mostly small systems that publish events other systems (quests, AI schedules, shops, weather-stat modifiers) consume.

Depends on: M1. Scheduling note: **Time** (§1–2) and **Navigation** (§5) should land early — Time right after M1 (shops/quests/AI daily routines want it), Navigation alongside M4a (agents must move for any interesting AI demo).

---

## 1. Time System **[core]**

- `TimeDef` (JSON): `minutesPerGameDay` (real-time scale), calendar structure (`daysPerSeason`, `seasons[]`), start datetime.
- `WorldClock` system: advances on tick; publishes `Time.MinuteTick`, `Time.HourStarted`, `Time.DayStarted`, `Time.SeasonStarted` on the event bus.
- Exposed to formulas/conditions: `Hour`, `Day`, `Season` identifiers (`"FormulaTrue": "Hour >= 20"`).
- Persisted in world delta; deterministic.

- [ ] Clock + calendar + bus events + formula exposure + persistence

## 2. Day/Night Cycle **[core]**

Not a renderer feature (we're headless) — a *trigger* layer:

- `DayPhaseDef`: named phases with hour ranges (`dawn 5–7, day 7–20, dusk 20–22, night 22–5`); phase changes publish `Time.PhaseChanged {phase}` and set global blackboard flags (`is_night`).
- Consumers: AI schedules (`require: ["IS_NIGHT"]` — NPCs go to bed at 20:00 is just a schedule gated on a condition fed by this flag), shop hours, spawn rules.
- Host adapter hook: `ILatticeHost` surface for ambient-light value (0–1 curve) so engines can drive visuals from the same source.

- [ ] Phase defs + flags + events + light-curve query

## 3. Weather System **[core]**

- `WeatherStateDef`: id, `statModifiers` (the plan-02 modifier primitives applied globally or by tag — `"Rain" reduces "FireDamage"` = a `PercentModifier` targeting a damage-type stat/tag), `stimulusEffects` (e.g. rain raises auditory-sensor noise floor → stealth gameplay emerges from data), allowed transitions + weights, duration range.
- `WeatherSystem`: weighted Markov transition on `Time.HourStarted` (seeded RNG); current state on global blackboard (`weather: "rain"`); publishes `Weather.Changed`.
- Season-conditional transition weights (§4).

- [ ] Weather defs + Markov transitions + global modifier application + sensor interaction + events

## 4. Seasons **[core-lite]**

- `SeasonDef`: id, loot-table overrides (winter swaps `loot_forest` entries), weather-weight table, stat/tag modifiers, content-swap hints for hosts (`prefabHints` — engine-agnostic: just data the host may use).
- Driven by calendar events from §1; overrides implemented as **registry overlays** (same mechanism as mod packs, plan 01 stretch / plan 06) — a season is a content layer that activates on `Time.SeasonStarted`.

- [ ] Season defs + loot/weather overlay activation + events

## 5. Navigation / Pathfinding **[core]**

Engine-agnostic strategy (research ch05 §5.3 informs the *interface*, not necessarily the v1 implementation):

- `INavigationService` (defined M0) is the seam: `FindPath(from, to, agentContext) → waypoints`, `TryReserveNode/Release`, `IsReachable`.
- **v1 implementation: grid-based A\*** in `Lattice.World` — sufficient for all demos, fully deterministic, testable. Scene JSON declares the grid (walkable cells, costs, tags).
- **Context-dependent costs** (HZD Part 9, scoped down): cell tags + `NavProfileDef` per agent profile — `{ "stealth_vegetation": { "patrol": "impassable", "investigating": 3.0 } }`. Same map, different paths by behavior state; the patrol-avoids-tall-grass stealth opportunity falls out of data.
- **Node reservation** is mandatory (not stretch): it is the exclusion mechanism that produces flanking/spread in M4c (research ch01 §1.5, F.E.A.R. Part 7).
- Multi-tier meshes / runtime rebuilds / aerial MIP-map navigation (HZD Parts 9–10): **[stretch]** — documented as host-engine responsibilities; the interface accommodates them (agent size class in `agentContext`).

- [ ] Grid def + A\* + context costs via nav profiles + reservation + `path <agent>` debug command
- [ ] Validation: unreachable-position warnings for placed objects/spawns

---

## M5 Acceptance
- Tavern day cycle: clock runs, NPCs' schedules flip on `is_night`, shop closes at dusk, rain starts (Markov) and audibly degrades guard hearing (sensor test), winter overlay swaps a loot table. Pathing respects stealth-grass tags per behavior state; two agents pathing to one node split via reservation.

## Stretch **[stretch]**
- Moving weather fronts / local weather zones
- Flow-field or HPA* for large agent counts
- Aerial navigation interface sketch (HZD MIP-map approach documented for host engines)
