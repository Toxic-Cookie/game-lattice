# Plan 04 — The AI "Brain" Suite (M4a–M4d)

> Implements concept **Phase 4**, architected directly from the research: the five-layer Ultimate System of `emergent-ai-guide/ch06` (Perception → World Model → Decision → Execution → Coordination), with the tiered-complexity rule from ch07 (the F.E.A.R. rat problem) as the governing constraint.

Depends on: M1 (events, registry, blackboard type), M3 (smart objects), M5-nav (grid navigation — co-schedule with M4a).

**Governing rule (ch01 §1.6, ch07 §7.1):** brains are tiered. An agent's JSON profile selects the cheapest brain that produces acceptable behavior — FSM (< ~10 behaviors), BT (10–30), GOAP/HTN (30+ or planning required). The validation suite warns on mismatches.

**Governing rule (ch01 §1.5):** no agent ever reads another agent's state. Coordination happens only through world state, blackboards (with latency), and reservation/exclusion.

---

## M4a — World Model, Sensors, FSM Family, Schedules

### 1. Agent World Model **[core]**

Two representations, per ch06 (fast flags + rich dictionary):

- `WorldState` : string→value predicate map (per agent — its *beliefs*, not ground truth; populated only by sensors and blackboard sync; never by direct world peeking — ch07 "too much information" anti-pattern).
- `ConditionSet` : ≤32-bit flag set (Half-Life pattern) for fast schedule validity checks. Condition names → bit positions declared in data (`conditions.json`), 2 bits reserved per agent type for custom flags (mirrors `CBaseMonster`).

- [ ] `WorldState` (copy/apply/diff support — planners need cheap copies), `ConditionSet` (set/clear/hasAll/hasAny masks)
- [ ] Data-declared condition catalog; validation rejects >32 or duplicate bits

### 2. Sensors & Perception **[core]**

HZD information-packet model (ch05 §5.2, HZD case study Part 7):

- `StimulusPacket` on every perceivable entity: sourceId, sourceType, sourceState, position, velocity, threatLevel, timestamp. Updated by the entity's systems (movement, death, item drops).
- `SensorDef` (JSON, per agent profile): `{ kind: visual|auditory|smell|proximity, range, sensitivity, fov?, cooldown }`.
  - **Visual**: FOV cone + LOS raycast (via `IPhysicsQueryService`) + concealment flag check.
  - **Auditory**: range only, no LOS. Sound *events* are transient stimulus packets published on the bus (`Stimulus.Sound`).
  - **Smell = inaudible sound event** (Half-Life's trick — reuse, don't rebuild): scent packets through the auditory path with a different type filter.
  - **Proximity**: tiny-range touch.
- Sensitivity filters packet fidelity → `PerceivedInfo { confidence: full|partial|minimal }`; low confidence drives *investigate*, not *attack* (ch05).
- `SensorComponent.Update` does a spatial query first (ch07 anti-pattern 2 — never scan all entities), then integrates perceptions into `WorldState` + `ConditionSet`.

- [ ] Stimulus packets + 4 sensor kinds + sensitivity filtering + spatial query
- [ ] JSON sensor calibration per agent profile (the Watcher-vs-Stalker dial, HZD Part 7)
- [ ] Debug: `senses <agent>` — current detections, confidences, FOV/range dump

### 3. FSM Family **[core]**

Per ch02 — these are both standalone brains for simple agents *and* the execution substrate for planners:

- `Fsm<TOwner>` — state-as-delegate with `onEnter/onExit` hooks.
- `StackFsm<TOwner>` — push/pop with `onSuspend/onResume`, re-push guard (ch02 §2.3). Used everywhere interrupts exist.
- `HierarchicalFsm` **[stretch]** — regions; only if a demo agent actually needs it.
- **Data-driven simple brains**: `FsmBrainDef` JSON — states reference *steering primitives* (`Wander`, `FleeFrom`, `MoveTo`, `Idle`) and transitions reference condition primitives. This is the rat-tier brain: `{ wander ⇄ flee }` in ~10 lines of JSON.

- [ ] Generic FSM + StackFSM (unit-tested with the ant scenario from `fsm-theory-and-implementation.md` Part 8 — two ants, different stacks, different resume states)
- [ ] `FsmBrainDef` + steering primitives + JSON transitions

### 4. The Half-Life Pattern: Tasks, Schedules, Conditions **[core]**

The mid-tier deliberative brain (ch02 §2.5, Half-Life case study Parts 2–5):

- `ITask` (atomic, *unblendable* — the constraint is the feature): `Execute(agent, dt) → Running|Complete|Failed`. Task vocabulary in C#: `MoveTo`, `PlayAnimation`, `Wait`, `FaceEntity`, `UseSmartObject`, `PublishEvent`, `SetCondition`, `SelectNewSchedule` — referenced from JSON by string.
- `ScheduleDef` (JSON): `requireConditions` mask, `interruptConditions` mask, ordered task list with args.
- Agent loop per ch02 §2.5: sensors → conditions → validity check → (invalid|complete ⇒ select first selectable schedule, priority-ordered) → execute current task. The feedback loop *is* the reactivity — no event handlers.
- Meta-state gate (`Idle|Alert|Prone|Dead`) filters which schedules are selectable.

```json
{ "id": "schedule_combat_advance", "type": "schedule",
  "require": ["CAN_SEE_ENEMY"], "interrupt": ["NO_AMMO", "HEAVY_DAMAGE"],
  "tasks": [ { "task": "MoveTo", "target": "$attackPosition", "speed": "run" },
             { "task": "PlayAnimation", "anim": "shoot", "blocking": false },
             { "task": "SelectNewSchedule" } ] }
```

- [ ] Task vocabulary + `ScheduleDef` + selection/invalidation loop + meta-state gating
- [ ] Schedule trace debug (ch07 §7.3): per-agent log of `schedule → invalidated-by → next`
- [ ] Simulation test: the full Half-Life sequence (patrol → hear sound → investigate → see enemy → combat → enemy hides → reposition) from the case study Part 4, expressed purely in content JSON

### M4a Acceptance
- Demo: guard agent (schedules) + 3 rats (FsmBrain) in a grid scene; player-controlled noise/visibility stimuli; schedule trace shows correct invalidation chains; rats never allocate planner structures (perf assertion — the rat problem test).

---

## M4b — Behavior Trees & Utility

### 5. Behavior Trees **[core]**

Industry-default mid-tier (ch05 §5.5):

- Nodes: `Sequence`, `Selector`, `Inverter`, `RepeatUntilFail`, `Cooldown`, `ConditionGate` decorators; leaves = **the same task & condition primitives as schedules** (one vocabulary, three brain tiers — key reuse decision).
- `BehaviorTreeDef` JSON (nested node objects); reusable sub-trees by reference (`"subtree": "bt_investigate"`).
- Tick-rate decoupling: BT agents tick at configurable frequency (ch06 §6.9).

- [ ] Node implementations + JSON loader + subtree refs + per-agent tick rate
- [ ] Debug: live tree print with last-tick status per node

### 6. Utility Functions **[core]**

Two patterns from ch05 §5.4, both data-driven:

- `UtilityEvaluatorDef`: weighted factors, each a formula over agent stats/world state returning 0–1 (normalization validated). Used as: (a) **goal priority** inputs for GOAP/HTN, (b) **planner preconditions** (HZD attack-interest gate: no attack plan unless score ≥ threshold), (c) standalone activity selection.
- **Need-based (Sims) pattern**: `NeedDef` (decay rates) + `ActivityDef` (satisfies map, cost) + selector `score = Σ urgency × satisfaction / cost`. This powers tavern-NPC daily life in the demo.

- [ ] Evaluator defs + formula factors + threshold gates
- [ ] Needs/activities system + selector
- [ ] Debug: utility scoreboard per agent (all candidate scores, chosen one highlighted)

### M4b Acceptance
- Tavern patron NPC runs entirely on needs+BT (drink when thirsty, sit when tired, chat when social need low) — defined in JSON; utility scoreboard explains every choice.

---

## M4c — GOAP

Per ch03 + F.E.A.R. case study. The high-emergence tier.

### 7. Actions, Goals, Planner **[core]**

- `GoapActionDef` (JSON): preconditions (predicate map), effects (predicate map), `cost` (number *or formula* — personality via cost profiles, ch03 §3.7: cowardly vs aggressive is data), execution binding (which task/animation/smart-object realizes it).
- `GoapGoalDef`: desired state + priority formula (returns 0 when irrelevant — ch06 checklist) + `replanRequired` condition list.
- **A\* planner** over predicate states: mismatch-count heuristic, `MAX_PLAN_LENGTH` (default 4, ch07 anti-pattern 5), `MAX_OPEN_NODES`, replan cooldown (anti-pattern 1). Pure function — heavy table-driven unit tests, including the canonical cost-3-beats-cost-5 example (ch03 §3.3).
- **Goal hysteresis** (ch07 §7.4): don't switch goals unless new priority exceeds current by threshold.

### 8. Execution Layer: the 3-State FSM **[core]**

F.E.A.R.'s insight — all behavior is `GoTo | Animate | UseSmartObject` (case study Part 4):

- `ExecutionLayer` drives the current action through those three states via `INavigationService` / `IAnimationService` / smart-object API. Non-interruptible animations block replanning (ch05 §5.6).
- The same layer executes HTN primitive tasks in M4d — build once.

### 9. Smart Objects as Actions **[core]**

- `SmartObjectDef.toGoapAction()` — objects in sensor range surface as plannable actions with their own pre/effects (plan 03 §5 fields). Reservation (`maxUsers`) is the exclusion coordinator.

### 10. Three Validation Mechanisms **[core]** (ch03 §3.6 — all three, non-negotiable)

1. Pre-execution simulation of the plan on a world-state copy.
2. Continuous `replanRequired` monitoring (gated by animation interruptibility, with cooldown — *and a relevance filter so we don't reproduce the actual rat bug*, F.E.A.R. Part 8).
3. Per-action precondition re-check at activation.

### 11. Agent Profiles & Action Subsets **[core]**

- `AgentProfileDef`: brain tier, sensor suite, action-subset ID list (F.E.A.R.'s database editor pattern, Part 5), goal set, cost profile, utility evaluators. **This file is the whole personality of an NPC type.**

- [ ] Planner + defs + hysteresis + budgets (unit tests)
- [ ] ExecutionLayer (3 states) + smart-object actions + reservation
- [ ] All three validation mechanisms + relevance-filtered replan
- [ ] GOAP decision dump (ch07 §7.4 verbatim): world state, goal priorities, plan with costs, per-action possible/missing-precondition report — exposed as `dump <agent>` demo command
- [ ] Simulation test: two soldiers + one player target + two attack-position smart objects ⇒ flanking emerges from exclusion (F.E.A.R. Part 7 sequence as an assertion: agents end on *different* nodes)

### M4c Acceptance
- Dungeon skirmish demo: 2–3 GOAP soldiers vs player dummy — cover use, flanking via reservation, reload/retreat plans; every decision reconstructable from the dump; replan cost bounded (perf test at 20 agents).

---

## M4d — HTN, Hierarchy, Coordination, Meta-Awareness

### 12. HTN Planner **[core]** (ch04, HZD case study)

- `PrimitiveTaskDef` (preconditions/effects/operator binding — same execution layer as GOAP) and `CompoundTaskDef` with ordered `methods: [{ preconditions, subtasks[] }]`.
- Depth-first decomposition with backtracking (ch04 §4.3), effects tracked through decomposition; recursion-depth budget.
- Designers/LLMs author *methods* (macros) — the control-vs-emergence trade is the point (ch01 §1.6).
- [ ] Planner + defs + decomposition trace logging (ch07 §7.5: indented decompose/✓/✗ log)

### 13. Group Agents, Roles, Blackboards **[core]**

- `GroupAgent` (non-physical, HZD Part 2): member list, **scoped Blackboard** (the plan-03 type), `RoleDef` assignments with **slot limits** (structural emergence — herd shape from limits, ch04 §4.6), alert level (`Relaxed→Alerted→Combat→Search` per HZD Part 5), group-level HTN for role rebalancing.
- Individuals sync *from* blackboard with staleness thresholds (latency is a feature: unwitnessed kills don't alert the herd — HZD Part 5). Role→goal mapping in data.
- [ ] GroupAgent + role slots + alert escalation/de-escalation + blackboard sync with per-key staleness (JSON)
- [ ] Blackboard & role inspectors (ch07 §7.6) as demo commands

### 14. The Collective **[core-lite]** (ch04 §4.5, HZD Part 4)

- Spawn sites (JSON), AI budget enforcement (despawn farthest-from-player), **passport** matching to recycle isolated agents into compatible groups.
- v1 scope: single-scene ecosystems; open-world streaming concerns **[stretch]**.
- [ ] Collective + passports + spawn/recycle/budget + tiered update scheduler (ch06 §6.9: every-frame sensors/execution, /3 validity, /6 goals, /10 groups, /30 collective)

### 15. Meta Player Awareness **[core-lite]** (concept Phase 4, unique item)

NPC reactions to *player behavior patterns* (e.g., NPC gets annoyed if the player walks away mid-conversation):

- `MetaSensorDef`: declarative detectors over player-input-derived events — `{ watch: "Dialogue.PlayerLookedAway", window: 5.0, threshold: 2, setCondition: "PLAYER_RUDE" }`. Detectors publish conditions/world-state predicates; existing brains (schedules/BT/GOAP) react through their normal machinery — no special code path.
- [ ] Meta-sensor defs + a demo dialogue interruption ("Hey, I'm talking to you!")

### M4d Acceptance
- Herd demo: mixed group (grazers/watchers/sawtooth-alike) forms core/ring structure purely from role slots; killing an unwitnessed watcher does **not** alert the herd (latency test); witnessed attack escalates alert level and restructures roles; isolated survivor gets recycled into another group via passport.

---

## Debug Tooling Summary (built *with* each system, per ch07 §7.10)

`state <agent>` (FSM/stack/schedule) · `dump <agent>` (GOAP) · `trace <agent>` (HTN decomposition) · `bb <group>` (blackboard ages/staleness) · `roles <group>` · `senses <agent>` · `utility <agent>` · `events` (bus ring buffer) · replay log (timestamped decisions) **[stretch]**.

## Key Tests (research-derived)
- Ant stack-resume (FSM theory Part 8) · Half-Life pipeline sequence (Part 4) · A* cost example (ch03 §3.3) · flanking-by-exclusion (F.E.A.R. Part 7) · rat-tier perf guard (Part 8) · goal hysteresis/oscillation (ch07 §7.4) · blackboard latency stealth (HZD Part 5) · role-slot formation (ch04 §4.6) · determinism across all of the above.
