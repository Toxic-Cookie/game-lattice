# Plan 07 — Demonstration & Polish (M7)

> Implements concept **Phase 7**. The demos are not toys — they are the *integration test suite* and the reference content an LLM (or human) studies to learn the framework. Each demo exercises a distinct system cluster and runs headless in `Lattice.Demo` (and therefore in CI as simulation tests).

Depends on: everything (M1–M6).

---

## 1. Demo Scene: The Tavern

**Exercises:** dialogue (Yarn), trade, time/day-night, needs-based utility AI, meta player awareness.

- Innkeeper: shop with Charisma pricing (plan 02 §6), Yarn dialogue with blackboard-gated branches, schedule flips at night (`is_night` condition → sweep floors → go to bed at 20:00).
- 2–3 patrons: needs/activity utility brains (drink/sit/chat) — plan 04 M4b.
- Meta-awareness beat: innkeeper interrupts dialogue if player "looks away" twice (plan 04 §15).
- Rain event degrades hearing; a patron comments via event-triggered Yarn node (weather → narrative coupling).

- [x] Scene JSON + content; scripted simulation test: full day cycle, all NPCs complete ≥1 full need loop; dialogue + trade transcript golden file *(`lifecycle_tavern` + `TavernDemoTests`; golden at `tests/Lattice.Demos.Tests/golden/tavern-transcript.txt`)*

## 2. Demo Scene: The Dungeon

**Exercises:** combat, GOAP, loot, smart objects, navigation/reservation, status effects.

- 3 GOAP soldiers (profile: cover/flank/reload/grenade-equivalent action subset) vs player entity; cover nodes as smart objects.
- Flanking emerges via node reservation (assert: simultaneous attackers occupy distinct nodes — the F.E.A.R. Part 7 test at demo scale).
- One rat-tier FSM critter per room (assert: zero planner allocations for it).
- Kills roll loot tables; poison trap applies `status_poison`; boss-lite uses HTN with two methods (ranged-preferred / melee-fallback) to show method selection.

- [x] Scene + profiles + simulation tests (flanking, rat-tier guard via `AiRuntime.PlannerInvocationsByAgent`) *(`lifecycle_dungeon` + `DungeonDemoTests`; boss-lite = `htn_boss` ranged/melee methods)*

## 3. Demo Scene: The Quest-Giver

**Exercises:** quest engine, event bus flow, JSON dialogue trees (the non-Yarn backend), persistence.

- `quest_wolves` chain end-to-end (accept → counter via `Entity.Died` events → report → reward), mid-quest save/load assertion.
- Demonstrates the event bus visibly: `events` command transcript included in docs.

- [x] Scene + simulation test covering the full quest lifecycle incl. save/load *(`lifecycle_quest` + `QuestGiverDemoTests`; the event-bus transcript is in `docs/architecture.md` §Observability)*

## 4. Herd Demo **(added beyond concept — M4d showcase)**

The coordination layer deserves its own demo (concept's three scenes don't cover it):

- Mixed herd with role slots → core/ring formation; stealth kill (unwitnessed, blackboard latency) vs witnessed attack (alert escalation, role restructure); passport recycling of a survivor.

- [ ] Scene + the M4d acceptance assertions as CI simulation tests *(the assertions run in `Lattice.Ai.Tests.HerdTests`; a dedicated showcase scene remains open)*

## 5. Documentation for LLMs **[core]**

The condensed authoring guide (concept Phase 7) — distinct from the auto-generated manifest:

- `docs/llm-guide.md`: how the interpreter model works (defs/instances, ID conventions, primitive vocabularies), how to author each def kind (one worked example each, lifted from demo content), the validation loop (`lattice validate` → fix → hot reload), and the "what NOT to do" list distilled from research ch07 (predicate bloat, brain-tier mismatch, hive-mind data sharing).
- Written to be pasted into a model's context: ≤ ~4k tokens, links to manifest/schemas for the rest.
- Companion `docs/architecture.md` for humans: layer diagram (perception→world model→decision→execution→coordination per ch06), system map, extension guide ("adding an effect primitive" walkthrough).

- [x] llm-guide + architecture doc + README refresh; the M6 LLM workflow test prompt archived as a worked example *(in `docs/llm-guide.md` §End-to-end example)*

## 6. Debug & Observability Assembly **[core]**

Per-system tools were built in their milestones (plan 04 list); M7 unifies:

- `lattice-inspect` mode in the demo: one command surface (`state/dump/trace/bb/roles/senses/utility/path/events/quests`), plus `watch <agent>` streaming view.
- Replay log **[stretch]**: timestamped decision log (ch07 §7.10) → `lattice replay <file>` reconstruction.
- Performance counters: per-tier AI update times, planner invocations/sec, event throughput — printed by `perf` command; CI perf-regression thresholds for the 20-agent dungeon scene.

- [~] Unified inspector + perf counters *(one command surface in the demo shell incl. `perf` planner counters; replay log and CI perf-regression thresholds remain open)*

## 7. Release Polish

- [~] Semantic version `0.1.0` (Directory.Build.props) + CHANGELOG shipped; license decision still open
- [ ] NuGet packaging of `Lattice.*` libs **[stretch]**
- [ ] **Godot host sample [core]** (`samples/Lattice.Godot`): minimal Godot 4 (C#) project implementing the five adapter interfaces (`ILatticeHost`, `IContentSource`, `INavigationService` via Godot navigation, `IAnimationService` via AnimationPlayer, `IPhysicsQueryService` via raycasts) and running one demo scene's content in-engine — the proof of the engine-agnostic seam (overview risk table)

---

## M7 Acceptance (= project v0.1 definition of done)
- All four demos run headless, green in CI as simulation tests, deterministic under fixed seed.
- The Godot host sample runs at least one demo scene's content in-engine through the adapter interfaces, with no changes to `Lattice.*` libraries.
- The M6 LLM workflow (manifest+schemas → new content → validate → hot reload) reproduced against the final docs.
- Every concept-doc Phase 1–7 **[core]** checkbox traceable to a shipped system + test (traceability table maintained in this file as items complete).

## Traceability snapshot (concept → plan)
| Concept item | Plan home |
|---|---|
| Registry, Event Bus, Lifecycle, Persistence, Formulas, Hot Reload | 01 |
| Stats, Modifiers, Inventory, Loot, Economy | 02 |
| Yarn, Quests, Dialogue, Blackboard, Interactions | 03 |
| Sensors, BT, FSM, GOAP, HTN, Utility, Meta-awareness | 04 |
| Time, Day/Night, Weather, Seasons, Navigation | 05 |
| Manifest, Schemas, Validation, Blueprints, UI Binding | 06 |
| Demos, LLM docs | 07 |
