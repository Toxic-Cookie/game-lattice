# Game Lattice — Content Manifest

108 defs. Every def needs `id`, `type`, and ideally a one-line `description`.
Defs may declare `"inherits": "<parent id>"` (same kind): objects deep-merge, scalars override,
arrays replace — or patch the parent's array with `{"$append": [...], "$remove": [...]}`.

## Registered defs by kind

### `activity` — 3

Something an agent can do about its needs: candidacy conditions, a cost formula, the needs it restores on completion, and the task list that realizes it — the same task vocabulary as schedules and BT leaves. Selector score = Σ (need urgency × satisfaction) / cost (ch05 §5.4).

- `activity_chat` — Join the table talk.
- `activity_drink` — Walk to the bar and drink. Selector score = thirst urgency × 0.7 / 1.
- `activity_rest` — Sit on the corner chair. Costlier, so it only wins when rest urgency is high.

### `agent` — 8

An agent profile is the whole personality of an NPC type (plan/04 §11): brain tier, sensor calibration, behavior content, movement. Tiered brains are the governing rule (the F.E.A.R. rat problem, ch07 §7.1): "fsm" for simple agents, "schedules" for deliberative ones; M4b–M4d add "bt", "goap", and "htn".

- `profile_beast` — Herd member (M4d): role and alerts arrive only via the group blackboard.
- `profile_boss` — Boss-lite: HTN method selection is visible in the decomposition trace — ranged while arrows last, melee after.
- `profile_forager` — HTN villager (M4d): the root compound's method order is the whole personality.
- `profile_guard` — Deliberative guard: Half-Life schedules, sharp eyes, decent ears.
- `profile_innkeeper` — The Wolf's Rest innkeeper: tends bar by day, sweeps at dusk, sleeps at night — the whole routine is day-phase flags feeding schedule gates.
- `profile_patron` — Tavern patron: needs-driven utility selection inside a behavior tree (M4b).
- `profile_rat` — Rat-tier critter: two-state FSM, dim eyes, zero planner overhead (the F.E.A.R. lesson).
- `profile_soldier` — GOAP soldier (M4c): goals + action subset + cost profile = the whole personality.

### `btree` — 2

A behavior tree (plan/04 §5) as nested node objects. Node shapes: composites {"node":"Sequence"|"Selector","children":[...]}, decorators {"node":"Inverter"|"RepeatUntilFail","child":{...}}, {"node":"Cooldown","seconds":n,"child":{...}}, {"node":"ConditionGate","when":[conditions],"child":{...}}, leaves {"task":...} / {"condition":...} (the same primitive vocabularies as schedules — one vocabulary, three brain tiers), and {"subtree":"bt_other"} references. Semantics: Sequences remember their running child between ticks; Selectors re-evaluate higher-priority children every tick and preempt a running lower-priority branch; ConditionGate decorators abort their running subtree when the gate fails — together the BT analog of schedule interrupt masks.

- `bt_loiter` — Reusable idle filler subtree.
- `bt_patron` — Tavern patron: flee threats, otherwise serve needs, otherwise loiter. Gates re-check every think and abort the running subtree.

### `collective` — 1

The Collective (ch04 §4.5, HZD Part 4): spawn sites that assemble groups, a global AI budget, and passport recycling for stranded agents.

- `collective_plains` — Spawns and maintains the plains herds within an AI budget.

### `conditions` — 1

The data-declared condition catalog: names map to bit positions in declaration order. One catalog per profile (default: conditions_default); at most 32 names (validation enforces).

- `conditions_default` — Default condition catalog (max 32 names; bit = declaration order).

### `costprofile` — 1

Personality as data (ch03 §3.7, F.E.A.R.'s per-archetype database): a profile-level map of action def ID → replacement cost formula. A cowardly and an aggressive soldier share the same action set and differ only in this file.

- `costprofile_brave` — Retreat is a last resort for these soldiers.

### `dayphases` — 1

Day-phase trigger layer (plan/05 §2): named phases over hour ranges. The active phase sets global flags (is_night, ...) and publishes Time.PhaseChanged; the light curve is data hosts may sample for visuals.

- `dayphases_default`

### `dialogue` — 1

JSON node-tree dialogue (plan/03 §3) — the secondary, machine-friendly dialogue format. Yarn is the primary authoring format; both run through the same DialogueRunner. Options and effects reuse the shared condition/effect primitives.

- `tree_guard` — JSON dialogue-tree sample (machine-friendly format).

### `entity` — 16

RPG-extended entity template, replacing the core "entity" def kind: adds loot, starting items, and auto-equipped gear. Stat keys in stats are stat def IDs under the RPG convention.

- `entity_attack_node` — A tactical position; surfaces to GOAP planners via so_attack_node.
- `entity_beast` — Herd animal (profile_beast: role-aware FSM, collective-spawned).
- `entity_boss` — Boss-lite (profile_boss: HTN with ranged-preferred / melee-fallback methods).
- `entity_chest`
- `entity_delver` (inherits `entity_player`) — The dungeon-delving player: the base player blueprint plus the 'intruder' tag the soldiers hunt and enough Constitution to survive the crossfire.
- `entity_dummy` — Training dummy the skirmish soldiers treat as an intruder.
- `entity_forager` — HTN forager (profile_forager): berry run with a threat-hiding method.
- `entity_guard` — Patrolling guard (profile_guard: schedule brain).
- `entity_innkeeper` — Wolf's Rest innkeeper (profile_innkeeper): bartender by day, sweeper at dusk, asleep at night; quest giver and shopkeeper.
- `entity_patron` — Tavern patron (profile_patron: needs + behavior tree). Low Charisma: pays full price.
- `entity_player` — The player avatar.
- `entity_rat` — Skittish rat (profile_rat: two-state FSM brain).
- `entity_soldier` — GOAP soldier (profile_soldier): plans attack-position use, fire, reload, retreat.
- `entity_trap` — Floor plate bound to so_poison_trap.
- `entity_wolf` — A common wolf; bites are poisonous. Drops loot_wolf.
- `entity_wolf_alpha` (inherits `entity_wolf`) — Blueprint demo (plan/06 §4): the base wolf plus overrides and an array patch.

### `fsmbrain` — 2

Data-driven simple FSM brain — the rat tier (ch07 §7.1): states pair a steering primitive with condition-gated transitions. ~10 lines of JSON per critter, zero planner overhead.

- `fsmbrain_beast` — Role-aware critter: graze or stand watch by assignment, flee on personal or group alert.
- `fsmbrain_rat` — Wander until something threatening appears; flee until it's gone.

### `goapaction` — 11

One plannable GOAP action (ch03, F.E.A.R. case study): symbolic preconditions and effects over the agent's predicate state (catalog condition names and belief keys; scalar values, missing booleans read as false), a cost formula (personality lives in cost — see CostProfileDef), and the execution binding the 3-state layer realizes (GoTo → Animate → effects).

- `action_carry_home`
- `action_charge` — Close the distance to the perceived enemy.
- `action_claw`
- `action_gather`
- `action_goto_berries`
- `action_lurk` — Idle filler so the root compound always decomposes to something.
- `action_open_fire` — Shoot from a held attack position; really damages the perceived enemy.
- `action_reload`
- `action_retreat` — Fall back to spawn. Expensive for brave profiles — personality is a cost number.
- `action_run_home`
- `action_shoot_bow` — Loose the last arrow: only plannable while has_arrows holds, and shooting spends it.

### `goapgoal` — 2

A GOAP goal: the desired predicate state, a priority formula that returns 0 when irrelevant (the ch06 checklist rule), and the relevance-filtered replan triggers — only the condition names listed here force a replan while this goal is active (the F.E.A.R. Part 8 lesson: replanning on every world change is how you get the rat bug).

- `goal_eliminate_intruder` — Fight while an enemy is perceived. Priority 0 (irrelevant) otherwise — the ch06 checklist rule.
- `goal_survive` — Break contact after taking damage; cost profiles decide who actually runs.

### `group` — 1

A group archetype (HZD Part 2): which roles exist (in fill-priority order), how stale shared knowledge may get, and how alert decays. The runtime group agent is non-physical — a member list, a scoped blackboard, an alert level, and role assignments.

- `group_herd` — Plains herd. Knowledge travels only through the blackboard, so unwitnessed kills never alert it.

### `htncompound` — 2

An HTN compound task (ch04, HZD case study): ordered methods, each a precondition-gated recipe of subtasks. Subtasks reference either another compound or a GoapActionDef — HTN primitives ARE GOAP actions, so both planners share one action vocabulary and one execution layer. Designers/LLMs author the methods; that's the control-vs-emergence dial (ch01 §1.6).

- `htn_boss` — Method order is the doctrine (plan/07 §2): shoot while arrows last, fall back to melee, otherwise lurk in the dark.
- `htn_forage` — Method order is the priority: hide if threatened, otherwise run the forage routine.

### `item` — 6

A data-defined item (plan/02 §4): tags, optional equipment slot, use-actions (effect primitives), and equip effects (modifier primitives active while worn). Currency is just an item with the "currency" tag.

- `item_ale`
- `item_gold`
- `item_healing_potion`
- `item_iron_sword`
- `item_wolf_fang`
- `item_wolf_pelt`

### `lifecycle` — 4

JSON-defined initialization: which flags the world starts with and what gets spawned (plan/01 §3). startingInventory joins the schema in M2.

- `lifecycle_default` — Default boot: a player and two wolves in the test scene.
- `lifecycle_dungeon` — Demo scene 2 — The Dungeon (plan/07 §2): GOAP combat with flanking from smart-object reservation, rat-tier FSM critters, loot on kill, a poison trap, and an HTN boss.
- `lifecycle_quest` — Demo scene 3 — The Quest-Giver (plan/07 §3): the quest_wolves chain end-to-end over the event bus, with mid-quest save/load.
- `lifecycle_tavern` — Demo scene 1 — The Tavern (plan/07 §1): dialogue, trade, day-night schedules, needs-driven patrons, meta player awareness, weather→narrative coupling.

### `loot` — 5

Weighted loot table (plan/02 §5). Each roll picks one eligible entry by weight; entries may grant an item (amount = formula, dice allowed), recurse into another table, or grant nothing (weight-only entry).

- `loot_boss`
- `loot_rare`
- `loot_soldier` — Kills roll loot tables (plan/07 §2): a soldier always carries pay.
- `loot_wolf`
- `loot_wolf_winter` — Winter overlay target: season_winter redirects loot_wolf here (thicker pelts, leaner purses).

### `metasensor` — 2

A declarative detector over player-behavior events (concept Phase 4): when Watch fires Threshold times within Window seconds for an agent, SetCondition is set on that agent — and existing brains react through their normal machinery. No special code path.

- `metasensor_lookaway` — Meta player awareness (plan/07 §1): look away from the conversation twice in half a minute and the innkeeper notices.
- `metasensor_poke` — Poke the guard three times in five seconds and it stops being polite.

### `navgrid` — 1

A walkable grid declared as rows of legend characters (plan/05 §5). The lowest-sorted navgrid ID is the active grid.

- `navgrid_main` — Tavern surroundings: a wall block at world (10..13, -10..-7) and a stealth-grass patch at (-12..-7, 8..11). Row 0 = Z -16.

### `navprofile` — 1

Context-dependent traversal costs (HZD Part 9, scoped down): per cell tag, per behavior state, a cost multiplier or "impassable". Binds to agent profiles by ID — same map, different paths by behavior state.

- `navprofile_guard` — Guards keep off the tall grass on routine patrol but wade through when alert (HZD stealth pattern).

### `need` — 3

A decaying motive (the Sims pattern, ch05 §5.4): value 1 = satisfied, 0 = desperate; urgency = 1 − value. Decayed every tick by the agent system for agents whose profile declares the need.

- `need_rest` — Tiredness. Decays slowly; restored by sitting down.
- `need_social` — Loneliness. Restored by chatting at the table.
- `need_thirst` — Drink urgency. Decays fast — the tavern's reason to exist.

### `quest` — 1

Step-based quest (plan/03 §4): each step has a completion condition and completion effects; optional event-driven counters feed the global blackboard (no entity polling).

- `quest_wolves` — The innkeeper pays 25 gold for culling three wolves.

### `role` — 2

A role members of a group can fill (ch04 §4.6, HZD Part 2). Slot limits are the whole mechanism: the herd's core/ring structure is emergent from "2 watchers, everyone else grazes" — no formation code.

- `role_grazer` — Everyone else: graze near the core.
- `role_watcher` — Sentries: 2 slots, posted on a ring around the herd centroid. The herd's shape IS this slot limit.

### `schedule` — 10

A Half-Life schedule (ch02 §2.5): a condition-gated macro-behavior. Selection requires all Require conditions; any Interrupt condition invalidates it mid-run. The invalidation feedback loop is the reactivity — no event handlers.

- `schedule_combat` — Enemy visible: close in and attack.
- `schedule_innkeeper_bed` — Night routine: head to the back room and sleep until dawn.
- `schedule_innkeeper_scold` — Meta-awareness beat (plan/07 §1): the player looked away twice mid-conversation (ANNOYED via metasensor_lookaway) — the innkeeper snaps and breaks off the dialogue.
- `schedule_innkeeper_sweep` — Dusk routine (IS_DUSK from the is_dusk day-phase flag): sweep the floor before closing.
- `schedule_investigate` — Heard something: run to the sound and look around.
- `schedule_patrol` — Default beat: walk the route, look around, repeat.
- `schedule_scold` — Meta-awareness reaction: the player has been pestering this NPC (ANNOYED via metasensor_poke).
- `schedule_search` — Lost the enemy: sweep their last known position.
- `schedule_sleep` — Off-duty at night (IS_NIGHT fed from the is_night flag via flagConditions). Outranks patrol, yields to anything urgent.
- `schedule_tend_bar` — The innkeeper's default beat: stand behind the bar and serve.

### `season` — 2

A season (plan/05 §4): a content layer activated by the calendar. Def redirects are the v1 overlay mechanism (winter resolves loot_forest to loot_forest_winter); weather bias multiplies transition weights; prefabHints are opaque data for hosts.

- `season_summer`
- `season_winter` — The overlay season: wolves drop winter loot, rain doubles.

### `shop` — 2

Shop definition (plan/02 §6): stock plus buy/sell price formulas evaluated with the item's BasePrice and the trading entity's stats in scope.

- `shop_innkeeper` — The innkeeper's bar: Charisma haggles the ale price down; closed for the night (openWhen reads the is_night day-phase flag).
- `shop_trader` — Charisma lowers buy prices and raises sell prices.

### `slot` — 2

An equipment slot, declared in data so slot sets are moddable (plan/02 §4).

- `slot_chest`
- `slot_main_hand`

### `smartobject` — 3

Smart object (plan/03 §5): self-describing interactions bound to an entity template. Player-facing verbs carry conditions + effects; the AI-facing fields (approach, animation, world-state pre/effects) are consumed by the M4 planners.

- `so_attack_node` — GOAP-plannable tactical node; maxUsers 1 makes reservation the flanking coordinator.
- `so_chest` — One-time gold cache; opening is gated on a world flag.
- `so_poison_trap` — Stepping on the plate envenoms the actor — statuses, interactions, and events composing (plan/07 §2).

### `stat` — 7

A stat declared in data (plan/02 §1). The Key is the identifier formulas use (Str, HP); the def id (stat_str) is what other defs reference. Convention: IDs in structural fields, keys in formula strings.

- `stat_carry` — Derived from Strength.
- `stat_cha`
- `stat_con`
- `stat_hp` — Vital: reaching 0 kills the entity.
- `stat_int`
- `stat_level`
- `stat_str`

### `status` — 1

Data-driven status effect (plan/02 §2): a duration, a stacking policy, and a list of logic primitives (modifiers, periodic effects, tags).

- `status_poison` — Deals 2 damage per second for 6 seconds.

### `time` — 1

The world clock's shape (plan/05 §1): real-time scale, calendar, start. One per world (the lowest-sorted ID wins if several load).

- `time_default` — Demo clock: a game day every 12 real minutes; 2-day seasons for fast demos.

### `utility` — 1

A reusable scoring function (ch05 §5.4): weighted factors, each a formula over the agent's needs (by key), stats, beliefs, and global flags, clamped to 0–1. The score is the weighted average, so it is itself 0–1. Used standalone through the UtilityAtLeast condition (threshold gates — the HZD attack-interest pattern) and as goal-priority input in M4c/M4d.

- `utility_patron_motivation` — How much the patron's needs demand attention (0–1). Thirst weighs double. Gates PerformActivity in bt_patron.

### `weather` — 2

One weather state (plan/05 §3): Markov transitions with weights, a duration range, global flags held while active (formulas and sensors read them — sense_auditory_mult is how rain degrades hearing), and tag-scoped effect primitives at the boundaries.

- `weather_clear`
- `weather_rain` — Rain dulls hearing and sight — stealth weather, straight from data.

## Blueprint hierarchies

- `entity_player` -> `entity_delver`
- `entity_wolf` -> `entity_wolf_alpha`

## Effect primitives (`"type"` discriminator)

- **DealDamage** — Subtract a rolled amount from the target's vital stat (death fires Entity.Died).
  - args: formula: damage amount (dice ok); stat?: stat def id (default stat_hp)
  - example: `{"type":"DealDamage","formula":"2d6 + Str"}`
- **Heal** — Restore a rolled amount of the target's vital stat (clamped to max).
  - args: formula: heal amount (dice ok); stat?: stat def id (default stat_hp)
  - example: `{"type":"Heal","formula":"1d8 + 2"}`
- **ModifyStat** — Permanently shift the target's base stat by a rolled delta.
  - args: stat: stat def id; formula: signed delta
  - example: `{"type":"ModifyStat","stat":"stat_str","formula":"1"}`
- **ApplyStatus** — Apply a status effect (duration/stacking per its def) to the target.
  - args: status: status def id
  - example: `{"type":"ApplyStatus","status":"status_poison"}`
- **RemoveStatus** — Remove a status effect from the target.
  - args: status: status def id
  - example: `{"type":"RemoveStatus","status":"status_poison"}`
- **GiveItem** — Add items to the target's inventory.
  - args: item: item def id; amount?: count formula (default 1, dice ok)
  - example: `{"type":"GiveItem","item":"item_gold","amount":"2d6"}`
- **RemoveItem** — Remove items from the target's inventory (no-op past zero).
  - args: item: item def id; amount?: count formula (default 1)
  - example: `{"type":"RemoveItem","item":"item_gold","amount":"10"}`
- **SetFlag** — Write a global blackboard flag (bool, number, or string).
  - args: flag: flag key; value: bool | number | string
  - example: `{"type":"SetFlag","flag":"chest_looted","value":true}`
- **PublishEvent** — Publish a bus event with optional scalar payload entries.
  - args: event: topic; payload?: object of scalars
  - example: `{"type":"PublishEvent","event":"Door.Opened","payload":{"door":"front"}}`
- **SpawnEntity** — Spawn an entity from a template at a position (default: the target's).
  - args: entity: entity def id; position?: [x, y, z]
  - example: `{"type":"SpawnEntity","entity":"entity_wolf","position":[4, 0, 2]}`
- **Teleport** — Move the target instantly to a position.
  - args: position: [x, y, z]
  - example: `{"type":"Teleport","position":[0, 0, 0]}`
- **AreaDamage** — Deal rolled damage to every entity within a radius of the target.
  - args: formula: damage amount (dice ok); radius?: world units (default 5); stat?: stat def id
  - example: `{"type":"AreaDamage","formula":"3d6","radius":4}`
- **StartQuest** — Start a quest for the player (no-op if already active or completed).
  - args: quest: quest def id
  - example: `{"type":"StartQuest","quest":"quest_wolves"}`

## Condition primitives (`"type"` discriminator)

- **AgentCondition** — True when the named condition bit is set on the subject agent (sensor-fed or manual).
  - args: condition: catalog condition name
  - example: `{"type":"AgentCondition","condition":"CAN_SEE_ENEMY"}`
- **AgentMeta** — True when the subject agent's meta state matches (persists via alertDecaySeconds — smoother than raw sensor bits).
  - args: is: "Idle" | "Alert"
  - example: `{"type":"AgentMeta","is":"Alert"}`
- **All** — True when every nested condition holds.
  - args: conditions: array of condition payloads
  - example: `{"type":"All","conditions":[{"type":"HasTag","tag":"npc"}]}`
- **Any** — True when at least one nested condition holds.
  - args: conditions: array of condition payloads
  - example: `{"type":"Any","conditions":[{"type":"HasTag","tag":"wolf"},{"type":"HasTag","tag":"bear"}]}`
- **BeliefEquals** — True when a scalar belief on the subject agent equals a value (e.g. group role assignments).
  - args: key: belief key; value: bool | number | string
  - example: `{"type":"BeliefEquals","key":"role","value":"role_watcher"}`
- **FlagEquals** — True when a global blackboard flag equals a scalar value.
  - args: flag: flag key; value: bool | number | string
  - example: `{"type":"FlagEquals","flag":"is_night","value":true}`
- **FormulaTrue** — True when a formula evaluates non-zero (subject stats + global flags in scope).
  - args: formula: expression (e.g. "Hour >= 20")
  - example: `{"type":"FormulaTrue","formula":"Str * 2 > Con"}`
- **HasItem** — True when the subject carries at least N of an item.
  - args: item: item def id; count?: minimum (default 1)
  - example: `{"type":"HasItem","item":"item_key","count":1}`
- **HasTag** — True when the subject entity carries a tag.
  - args: tag: tag string
  - example: `{"type":"HasTag","tag":"undead"}`
- **NeedBelow** — True when the agent's need value (1 = satisfied) is below the threshold.
  - args: need: need def id; threshold?: 0-1 (default 0.5)
  - example: `{"type":"NeedBelow","need":"need_thirst","threshold":0.3}`
- **Not** — Inverts a nested condition.
  - args: condition: a condition payload
  - example: `{"type":"Not","condition":{"type":"FlagEquals","flag":"is_night","value":true}}`
- **StatAtLeast** — True when the subject's current stat meets a rolled threshold.
  - args: stat: stat def id; value: threshold formula
  - example: `{"type":"StatAtLeast","stat":"stat_str","value":"5"}`
- **UtilityAtLeast** — True when a utility evaluator's weighted score (0-1) meets the threshold (agent subjects only).
  - args: evaluator: utility def id; threshold?: 0-1 (default 0.5)
  - example: `{"type":"UtilityAtLeast","evaluator":"utility_motivation","threshold":0.4}`

## Task primitives (`"task"` discriminator — schedules, BT leaves, activities)

- **ClearCondition** — Clear a sticky condition bit set by SetCondition.
  - args: condition: catalog condition name
  - example: `{"task":"ClearCondition","condition":"CUSTOM_FLAG"}`
- **FaceEntity** — Turn to face a target instantly.
  - args: target: same specs as MoveTo
  - example: `{"task":"FaceEntity","target":"enemy"}`
- **MoveTo** — Walk/run to a target; completes on arrival, fails when unreachable.
  - args: target: "patrol_point"|"enemy"|"threat"|"sound"|"scent"|"last_enemy"|"spawn"|"post"|"group_threat"|[x,y,z]; speed?: "walk"|"run"|number
  - example: `{"task":"MoveTo","target":"enemy","speed":"run"}`
- **NextPatrolPoint** — Advance to the next patrol waypoint (wraps around the profile's patrolPoints).
  - args: (no args)
  - example: `{"task":"NextPatrolPoint"}`
- **PerformActivity** — Run the need-based utility selector, commit to the best activity from the profile, and execute its task list.
  - args: (no args; candidates come from the profile's activities)
  - example: `{"task":"PerformActivity"}`
- **PlayAnimation** — Play an animation; completes when the host reports it finished. blocking animations suppress replanning.
  - args: anim: animation id; blocking?: bool (default true = non-interruptible)
  - example: `{"task":"PlayAnimation","anim":"attack"}`
- **PublishEvent** — Publish a bus event carrying the agent's instance id.
  - args: event: topic
  - example: `{"task":"PublishEvent","event":"Npc.Waved"}`
- **SelectNewSchedule** — End the current schedule so the brain re-selects (put it last in looping schedules).
  - args: (no args)
  - example: `{"task":"SelectNewSchedule"}`
- **SetCondition** — Set a sticky condition bit on the agent (survives sensor refresh; clear with ClearCondition).
  - args: condition: catalog condition name
  - example: `{"task":"SetCondition","condition":"CUSTOM_FLAG"}`
- **UseSmartObject** — Perform a verb on the nearest smart object within range.
  - args: verb?: interaction verb (default "interact"); range?: world units (default 2)
  - example: `{"task":"UseSmartObject","verb":"open","range":2}`
- **Wait** — Stand still for a number of simulated seconds.
  - args: seconds: duration (default 1)
  - example: `{"task":"Wait","seconds":2.5}`

## FSM steering primitives (`"type"` in fsmbrain states)

- **Idle** — Stand still.
  - args: (no args)
  - example: `{"type":"Idle"}`
- **Wander** — Drift randomly around the spawn point.
  - args: radius?: default 4; speed?: default walk; interval?: seconds between legs (default 1.5)
  - example: `{"type":"Wander","radius":4,"speed":1.2}`
- **FleeFrom** — Run away from the current threat/enemy/group-threat belief.
  - args: distance?: flee leg length (default 6); speed?: default run
  - example: `{"type":"FleeFrom","distance":8,"speed":3.5}`
- **MoveTo** — Walk toward a target spec (same specs as the MoveTo task).
  - args: target: symbol or [x,y,z]; speed?: number
  - example: `{"type":"MoveTo","target":"post","speed":1.4}`

## Catalogs

### Stats (use the def id in structural fields, the key in formulas)
- `stat_carry` -> formula key `Carry`
- `stat_cha` -> formula key `Charisma`
- `stat_con` -> formula key `Con`
- `stat_hp` -> formula key `HP` (vital)
- `stat_int` -> formula key `Int`
- `stat_level` -> formula key `Level`
- `stat_str` -> formula key `Str`

### Equipment slots
- `slot_chest`
- `slot_main_hand`

### Condition catalog `conditions_default` (11/32 bits)
`CAN_SEE_ENEMY`, `THREAT_KNOWN`, `HEAR_SOUND`, `SMELL_DETECTED`, `CONTACT`, `DAMAGED`, `GROUP_ALERT`, `ROLE_WATCHER`, `ANNOYED`, `IS_NIGHT`, `IS_DUSK`

### Event topics
- `Entity.Damaged` — an entity took damage {instanceId, amount}
- `Entity.Died` — a vital stat hit zero {instanceId, defId, killerId}
- `Stat.Changed` — a stat's current value changed {instanceId, stat, old, new}
- `Item.Acquired / Item.Removed / Item.Equipped / Item.Unequipped / Item.Used` — inventory changes {instanceId, item, ...}
- `Trade.Completed` — a shop transaction {shop, customerId, item, price, kind}
- `Interaction.Performed` — a smart-object verb ran {actorId, targetId, object, verb}
- `Quest.Started / Quest.StepCompleted / Quest.Completed` — quest progress {quest, step?}
- `Dialogue.Started / Dialogue.Ended` — conversation lifecycle {node}
- `Stimulus.Sound / Stimulus.Scent` — world stimuli for AI senses {x, y, z, loudness}
- `Time.MinuteTick / Time.HourStarted / Time.DayStarted / Time.SeasonStarted / Time.PhaseChanged` — calendar (M5)
- `Weather.Changed` — weather transition {weather}
- `Content.Reloaded` — hot reload applied

## Formula identifier scope

Formulas (NCalc syntax, dice like `2d6+1` allowed) resolve identifiers in this order:
1. **Stat keys** of the subject entity (see the stat catalog above).
2. **Need keys** of the subject agent (utility/GOAP scopes), e.g. `Thirst`.
3. **Condition names** of the subject agent as 0/1 (GOAP priorities/costs), e.g. `CAN_SEE_ENEMY * 50`.
4. **Numeric/bool beliefs** of the subject agent (GOAP/utility scopes).
5. **Global flags** (blackboard), including the clock: `Hour` (fractional), `Day`, `Season` (index),
   plus `is_<phase>` booleans and whatever content/weather writes.
Item price formulas additionally see `BasePrice`.

