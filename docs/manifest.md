# Game Lattice — Content Manifest

84 defs. Every def needs `id`, `type`, and ideally a one-line `description`.
Defs may declare `"inherits": "<parent id>"` (same kind): objects deep-merge, scalars override,
arrays replace — or patch the parent's array with `{"$append": [...], "$remove": [...]}`.

## Registered defs by kind

### `activity` — 3

- `activity_chat` — Join the table talk.
- `activity_drink` — Walk to the bar and drink. Selector score = thirst urgency × 0.7 / 1.
- `activity_rest` — Sit on the corner chair. Costlier, so it only wins when rest urgency is high.

### `agent` — 6

- `profile_beast` — Herd member (M4d): role and alerts arrive only via the group blackboard.
- `profile_forager` — HTN villager (M4d): the root compound's method order is the whole personality.
- `profile_guard` — Deliberative guard: Half-Life schedules, sharp eyes, decent ears.
- `profile_patron` — Tavern patron: needs-driven utility selection inside a behavior tree (M4b).
- `profile_rat` — Rat-tier critter: two-state FSM, dim eyes, zero planner overhead (the F.E.A.R. lesson).
- `profile_soldier` — GOAP soldier (M4c): goals + action subset + cost profile = the whole personality.

### `btree` — 2

- `bt_loiter` — Reusable idle filler subtree.
- `bt_patron` — Tavern patron: flee threats, otherwise serve needs, otherwise loiter. Gates re-check every think and abort the running subtree.

### `collective` — 1

- `collective_plains` — Spawns and maintains the plains herds within an AI budget.

### `conditions` — 1

- `conditions_default` — Default condition catalog (max 32 names; bit = declaration order).

### `costprofile` — 1

- `costprofile_brave` — Retreat is a last resort for these soldiers.

### `dayphases` — 1

- `dayphases_default`

### `dialogue` — 1

- `tree_guard` — JSON dialogue-tree sample (machine-friendly format).

### `entity` — 12

- `entity_attack_node` — A tactical position; surfaces to GOAP planners via so_attack_node.
- `entity_beast` — Herd animal (profile_beast: role-aware FSM, collective-spawned).
- `entity_chest`
- `entity_dummy` — Training dummy the skirmish soldiers treat as an intruder.
- `entity_forager` — HTN forager (profile_forager): berry run with a threat-hiding method.
- `entity_guard` — Patrolling guard (profile_guard: schedule brain).
- `entity_patron` — Tavern patron (profile_patron: needs + behavior tree).
- `entity_player` — The player avatar.
- `entity_rat` — Skittish rat (profile_rat: two-state FSM brain).
- `entity_soldier` — GOAP soldier (profile_soldier): plans attack-position use, fire, reload, retreat.
- `entity_wolf` — A common wolf; bites are poisonous. Drops loot_wolf.
- `entity_wolf_alpha` (inherits `entity_wolf`) — Blueprint demo (plan/06 §4): the base wolf plus overrides and an array patch.

### `fsmbrain` — 2

- `fsmbrain_beast` — Role-aware critter: graze or stand watch by assignment, flee on personal or group alert.
- `fsmbrain_rat` — Wander until something threatening appears; flee until it's gone.

### `goapaction` — 7

- `action_carry_home`
- `action_gather`
- `action_goto_berries`
- `action_open_fire` — Shoot from a held attack position; really damages the perceived enemy.
- `action_reload`
- `action_retreat` — Fall back to spawn. Expensive for brave profiles — personality is a cost number.
- `action_run_home`

### `goapgoal` — 2

- `goal_eliminate_intruder` — Fight while an enemy is perceived. Priority 0 (irrelevant) otherwise — the ch06 checklist rule.
- `goal_survive` — Break contact after taking damage; cost profiles decide who actually runs.

### `group` — 1

- `group_herd` — Plains herd. Knowledge travels only through the blackboard, so unwitnessed kills never alert it.

### `htncompound` — 1

- `htn_forage` — Method order is the priority: hide if threatened, otherwise run the forage routine.

### `item` — 5

- `item_gold`
- `item_healing_potion`
- `item_iron_sword`
- `item_wolf_fang`
- `item_wolf_pelt`

### `lifecycle` — 1

- `lifecycle_default` — Default boot: a player and two wolves in the test scene.

### `loot` — 3

- `loot_rare`
- `loot_wolf`
- `loot_wolf_winter` — Winter overlay target: season_winter redirects loot_wolf here (thicker pelts, leaner purses).

### `metasensor` — 1

- `metasensor_poke` — Poke the guard three times in five seconds and it stops being polite.

### `navgrid` — 1

- `navgrid_main` — Tavern surroundings: a wall block at world (10..13, -10..-7) and a stealth-grass patch at (-12..-7, 8..11). Row 0 = Z -16.

### `navprofile` — 1

- `navprofile_guard` — Guards keep off the tall grass on routine patrol but wade through when alert (HZD stealth pattern).

### `need` — 3

- `need_rest` — Tiredness. Decays slowly; restored by sitting down.
- `need_social` — Loneliness. Restored by chatting at the table.
- `need_thirst` — Drink urgency. Decays fast — the tavern's reason to exist.

### `quest` — 1

- `quest_wolves` — The innkeeper pays 25 gold for culling three wolves.

### `role` — 2

- `role_grazer` — Everyone else: graze near the core.
- `role_watcher` — Sentries: 2 slots, posted on a ring around the herd centroid. The herd's shape IS this slot limit.

### `schedule` — 6

- `schedule_combat` — Enemy visible: close in and attack.
- `schedule_investigate` — Heard something: run to the sound and look around.
- `schedule_patrol` — Default beat: walk the route, look around, repeat.
- `schedule_scold` — Meta-awareness reaction: the player has been pestering this NPC (ANNOYED via metasensor_poke).
- `schedule_search` — Lost the enemy: sweep their last known position.
- `schedule_sleep` — Off-duty at night (IS_NIGHT fed from the is_night flag via flagConditions). Outranks patrol, yields to anything urgent.

### `season` — 2

- `season_summer`
- `season_winter` — The overlay season: wolves drop winter loot, rain doubles.

### `shop` — 1

- `shop_trader` — Charisma lowers buy prices and raises sell prices.

### `slot` — 2

- `slot_chest`
- `slot_main_hand`

### `smartobject` — 2

- `so_attack_node` — GOAP-plannable tactical node; maxUsers 1 makes reservation the flanking coordinator.
- `so_chest` — One-time gold cache; opening is gated on a world flag.

### `stat` — 7

- `stat_carry` — Derived from Strength.
- `stat_cha`
- `stat_con`
- `stat_hp` — Vital: reaching 0 kills the entity.
- `stat_int`
- `stat_level`
- `stat_str`

### `status` — 1

- `status_poison` — Deals 2 damage per second for 6 seconds.

### `time` — 1

- `time_default` — Demo clock: a game day every 12 real minutes; 2-day seasons for fast demos.

### `utility` — 1

- `utility_patron_motivation` — How much the patron's needs demand attention (0–1). Thirst weighs double. Gates PerformActivity in bt_patron.

### `weather` — 2

- `weather_clear`
- `weather_rain` — Rain dulls hearing and sight — stealth weather, straight from data.

## Blueprint hierarchies

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

### Condition catalog `conditions_default` (10/32 bits)
`CAN_SEE_ENEMY`, `THREAT_KNOWN`, `HEAR_SOUND`, `SMELL_DETECTED`, `CONTACT`, `DAMAGED`, `GROUP_ALERT`, `ROLE_WATCHER`, `ANNOYED`, `IS_NIGHT`

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

