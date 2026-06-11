using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Narrative;
using Lattice.Rpg;
using Lattice.World;

namespace Lattice.Ai.Tests;

internal sealed class NullLogger : ILatticeLogger
{
    public static readonly NullLogger Instance = new();

    public void Log(LogLevel level, string message)
    {
    }
}

/// <summary>Test host with RPG + Narrative + AI attached over a temp content directory.</summary>
internal sealed class AiTestHost : IDisposable
{
    public AiTestHost(int seed = 1)
    {
        ContentRoot = Path.Combine(Path.GetTempPath(), "lattice-m4a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ContentRoot);
        Content = new DirectoryContentSource(ContentRoot, watch: false);
        Animation = new TimedStubAnimationService(animationDurationSeconds: 0.4);
        Services = new HostServices
        {
            Host = new StandaloneHost(seed, NullLogger.Instance),
            Content = Content,
            Navigation = new StraightLineNavigationService(),
            Animation = Animation,
            Physics = new PermissivePhysicsQueryService(),
        };
    }

    public string ContentRoot { get; }

    public DirectoryContentSource Content { get; }

    public TimedStubAnimationService Animation { get; }

    public HostServices Services { get; }

    public void WriteContent(string relativePath, string text)
    {
        var path = Path.Combine(ContentRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void WriteStandardContent()
    {
        WriteContent("stats.json", """
            [
              { "id": "stat_con", "type": "stat", "key": "Con", "min": "0", "max": "99", "default": "3" },
              { "id": "stat_hp", "type": "stat", "key": "HP", "min": "0", "max": "Con * 5 + 10", "default": "max", "vital": true }
            ]
            """);
        WriteContent("entities.json", """
            [
              { "id": "entity_player", "type": "entity", "name": "Player", "tags": ["player"], "stats": { "stat_con": 4 } },
              { "id": "entity_guard", "type": "entity", "name": "Guard", "tags": ["guard"], "stats": { "stat_con": 5 } },
              { "id": "entity_rat", "type": "entity", "name": "Rat", "tags": ["rat"], "stats": { "stat_con": 1 } },
              { "id": "entity_patron", "type": "entity", "name": "Patron", "tags": ["patron"], "stats": { "stat_con": 3 } },
              { "id": "entity_soldier", "type": "entity", "name": "Soldier", "tags": ["soldier"], "stats": { "stat_con": 6 } },
              { "id": "entity_dummy", "type": "entity", "name": "Dummy", "tags": ["intruder"], "stats": { "stat_con": 2 } },
              { "id": "entity_attack_node", "type": "entity", "name": "Attack Position", "tags": ["node"] }
            ]
            """);
        WriteContent("goapobjects.json", """
            { "id": "so_attack_node", "type": "smartobject", "entity": "entity_attack_node",
              "maxUsers": 1, "interactions": [],
              "animation": "aim", "aiEffects": { "in_attack_position": true } }
            """);
        WriteContent("goap.json", """
            [
              { "id": "goal_eliminate_intruder", "type": "goapgoal",
                "desired": { "intruder_down": true },
                "priority": "CAN_SEE_ENEMY * 50 + THREAT_KNOWN * 10",
                "replanRequired": ["DAMAGED"] },
              { "id": "goal_survive", "type": "goapgoal",
                "desired": { "safe": true }, "priority": "DAMAGED * 100" },
              { "id": "action_open_fire", "type": "goapaction",
                "preconditions": { "in_attack_position": true, "weapon_loaded": true },
                "effects": { "intruder_down": true, "weapon_loaded": false },
                "cost": "1", "animation": "shoot", "animationBlocking": true,
                "runEffects": [ { "type": "DealDamage", "formula": "2d6 + 4" } ] },
              { "id": "action_reload", "type": "goapaction",
                "preconditions": { "weapon_loaded": false },
                "effects": { "weapon_loaded": true }, "cost": "1", "animation": "reload" },
              { "id": "action_retreat", "type": "goapaction",
                "effects": { "safe": true }, "cost": "2", "moveTo": "spawn", "speed": "run" },
              { "id": "costprofile_brave", "type": "costprofile",
                "overrides": { "action_retreat": "20" } },
              { "id": "profile_soldier", "type": "agent", "entities": ["entity_soldier"],
                "brain": "goap",
                "goals": ["goal_eliminate_intruder", "goal_survive"],
                "actions": ["action_open_fire", "action_reload", "action_retreat"],
                "costProfile": "costprofile_brave",
                "initialBeliefs": { "weapon_loaded": true },
                "hostileTags": ["intruder"],
                "sensors": [ { "kind": "visual", "range": 14, "fov": 360, "sensitivity": 0.9 } ],
                "walkSpeed": 2.2, "runSpeed": 4.5, "alertDecaySeconds": 4.0,
                "replanCooldown": 0.5, "goalHysteresis": 5.0 }
            ]
            """);
        WriteContent("conditions.json", """
            { "id": "conditions_default", "type": "conditions",
              "names": ["CAN_SEE_ENEMY", "THREAT_KNOWN", "HEAR_SOUND", "SMELL_DETECTED", "CONTACT", "DAMAGED",
                        "CUSTOM_FLAG", "GROUP_ALERT", "ROLE_WATCHER", "ANNOYED", "IS_NIGHT"] }
            """);
        WriteContent("profiles.json", """
            [
              { "id": "profile_guard", "type": "agent", "entities": ["entity_guard"],
                "brain": "schedules",
                "schedules": ["schedule_combat", "schedule_investigate", "schedule_search", "schedule_sleep", "schedule_patrol"],
                "metaSensors": ["metasensor_poke"],
                "flagConditions": { "IS_NIGHT": "is_night" },
                "hostileTags": ["player"],
                "sensors": [ { "kind": "visual", "range": 10, "fov": 360, "sensitivity": 0.9 },
                             { "kind": "auditory", "range": 12 } ],
                "walkSpeed": 2.0, "runSpeed": 4.0, "alertDecaySeconds": 2.0,
                "patrolPoints": [[3, 0, 0], [-3, 0, 0]] },
              { "id": "profile_rat", "type": "agent", "entities": ["entity_rat"],
                "brain": "fsm", "fsmBrain": "fsmbrain_rat",
                "hostileTags": ["player"],
                "sensors": [ { "kind": "visual", "range": 6, "fov": 360, "sensitivity": 0.5 } ],
                "walkSpeed": 1.2, "runSpeed": 3.5, "alertDecaySeconds": 1.0 },
              { "id": "profile_patron", "type": "agent", "entities": ["entity_patron"],
                "brain": "bt", "behaviorTree": "bt_patron",
                "needs": ["need_thirst", "need_rest", "need_social"],
                "activities": ["activity_drink", "activity_rest", "activity_chat"],
                "hostileTags": ["player"],
                "sensors": [ { "kind": "visual", "range": 8, "fov": 360, "sensitivity": 0.6 } ],
                "walkSpeed": 2.0, "runSpeed": 4.0, "alertDecaySeconds": 2.0 }
            ]
            """);
        WriteContent("needs.json", """
            [
              { "id": "need_thirst", "type": "need", "key": "Thirst", "initial": 0.5, "decayPerSecond": 0.02 },
              { "id": "need_rest",   "type": "need", "key": "Rest",   "initial": 0.9, "decayPerSecond": 0.008 },
              { "id": "need_social", "type": "need", "key": "Social", "initial": 0.7, "decayPerSecond": 0.012 }
            ]
            """);
        WriteContent("activities.json", """
            [
              { "id": "activity_drink", "type": "activity", "satisfies": { "need_thirst": 0.7 }, "cost": "1",
                "tasks": [ { "task": "MoveTo", "target": [3, 0, 3], "speed": "walk" },
                           { "task": "PlayAnimation", "anim": "drink" },
                           { "task": "Wait", "seconds": 0.5 } ] },
              { "id": "activity_rest", "type": "activity", "satisfies": { "need_rest": 0.6 }, "cost": "1.5",
                "tasks": [ { "task": "MoveTo", "target": [-3, 0, 2], "speed": "walk" },
                           { "task": "Wait", "seconds": 0.5 } ] },
              { "id": "activity_chat", "type": "activity", "satisfies": { "need_social": 0.6 }, "cost": "1.2",
                "tasks": [ { "task": "MoveTo", "target": [0, 0, -3], "speed": "walk" },
                           { "task": "Wait", "seconds": 0.5 } ] }
            ]
            """);
        WriteContent("utilities.json", """
            { "id": "utility_patron_motivation", "type": "utility",
              "factors": [ { "formula": "1 - Thirst", "weight": 2 },
                           { "formula": "1 - Rest", "weight": 1 },
                           { "formula": "1 - Social", "weight": 1 } ] }
            """);
        WriteContent("btrees.json", """
            [
              { "id": "bt_patron", "type": "btree", "root": {
                  "node": "Selector", "children": [
                    { "node": "ConditionGate",
                      "when": [ { "type": "AgentCondition", "condition": "THREAT_KNOWN" } ],
                      "child": { "node": "Sequence", "children": [
                        { "task": "MoveTo", "target": [12, 0, 12], "speed": "run" },
                        { "task": "Wait", "seconds": 1.0 } ] } },
                    { "node": "ConditionGate",
                      "when": [ { "type": "UtilityAtLeast", "evaluator": "utility_patron_motivation", "threshold": 0.15 } ],
                      "child": { "task": "PerformActivity" } },
                    { "subtree": "bt_loiter" }
                  ] } },
              { "id": "bt_loiter", "type": "btree", "root": {
                  "node": "Sequence", "children": [
                    { "task": "PlayAnimation", "anim": "idle" },
                    { "task": "Wait", "seconds": 0.5 } ] } }
            ]
            """);
        WriteContent("schedules.json", """
            [
              { "id": "schedule_patrol", "type": "schedule", "priority": 10, "metaStates": ["Idle"],
                "interrupt": ["CAN_SEE_ENEMY", "THREAT_KNOWN", "HEAR_SOUND", "DAMAGED"],
                "tasks": [ { "task": "MoveTo", "target": "patrol_point", "speed": "walk" },
                           { "task": "Wait", "seconds": 0.3 },
                           { "task": "NextPatrolPoint" },
                           { "task": "SelectNewSchedule" } ] },
              { "id": "schedule_investigate", "type": "schedule", "priority": 50,
                "require": ["HEAR_SOUND"], "interrupt": ["CAN_SEE_ENEMY", "DAMAGED"],
                "tasks": [ { "task": "FaceEntity", "target": "sound" },
                           { "task": "MoveTo", "target": "sound", "speed": "run" },
                           { "task": "Wait", "seconds": 0.5 },
                           { "task": "SelectNewSchedule" } ] },
              { "id": "schedule_combat", "type": "schedule", "priority": 100,
                "require": ["CAN_SEE_ENEMY"], "interrupt": [],
                "tasks": [ { "task": "FaceEntity", "target": "enemy" },
                           { "task": "MoveTo", "target": "enemy", "speed": "run" },
                           { "task": "PlayAnimation", "anim": "attack" },
                           { "task": "SelectNewSchedule" } ] },
              { "id": "schedule_search", "type": "schedule", "priority": 20, "metaStates": ["Alert"],
                "interrupt": ["CAN_SEE_ENEMY", "HEAR_SOUND"],
                "tasks": [ { "task": "MoveTo", "target": "last_enemy", "speed": "run" },
                           { "task": "Wait", "seconds": 0.5 },
                           { "task": "SelectNewSchedule" } ] },
              { "id": "schedule_sleep", "type": "schedule", "priority": 15, "metaStates": ["Idle"],
                "require": ["IS_NIGHT"],
                "interrupt": ["CAN_SEE_ENEMY", "THREAT_KNOWN", "HEAR_SOUND", "DAMAGED"],
                "tasks": [ { "task": "PlayAnimation", "anim": "sleep" },
                           { "task": "Wait", "seconds": 1.0 },
                           { "task": "SelectNewSchedule" } ] }
            ]
            """);
        WriteContent("fsmbrains.json", """
            [
              { "id": "fsmbrain_rat", "type": "fsmbrain", "initial": "wander",
                "states": {
                  "wander": { "steering": { "type": "Wander", "speed": 1.2, "radius": 4, "interval": 0.5 },
                    "transitions": [ { "to": "flee", "when": [ { "type": "Any", "conditions": [
                        { "type": "AgentCondition", "condition": "THREAT_KNOWN" },
                        { "type": "AgentCondition", "condition": "CAN_SEE_ENEMY" } ] } ] } ] },
                  "flee": { "steering": { "type": "FleeFrom", "speed": 3.5, "distance": 6 },
                    "transitions": [ { "to": "wander", "when": [ { "type": "Not", "condition": { "type": "Any", "conditions": [
                        { "type": "AgentCondition", "condition": "THREAT_KNOWN" },
                        { "type": "AgentCondition", "condition": "CAN_SEE_ENEMY" } ] } } ] } ] }
                } }
            ]
            """);
        WriteContent("htn.json", """
            [
              { "id": "action_run_home", "type": "goapaction",
                "effects": { "hid": true }, "cost": "1", "moveTo": "spawn", "speed": "run" },
              { "id": "action_goto_berries", "type": "goapaction",
                "effects": { "at_berries": true }, "cost": "1", "moveTo": [6, 0, 0], "speed": "walk" },
              { "id": "action_gather", "type": "goapaction",
                "preconditions": { "at_berries": true }, "effects": { "gathered": true },
                "cost": "1", "animation": "gather" },
              { "id": "action_carry_home", "type": "goapaction",
                "preconditions": { "gathered": true },
                "effects": { "at_berries": false, "gathered": false, "deliveries_done": true },
                "cost": "1", "moveTo": "spawn", "speed": "walk" },
              { "id": "htn_forage", "type": "htncompound", "methods": [
                  { "name": "hide", "preconditions": { "THREAT_KNOWN": true }, "subtasks": ["action_run_home"] },
                  { "name": "forage_run", "subtasks": ["action_goto_berries", "action_gather", "action_carry_home"] } ] },
              { "id": "entity_forager", "type": "entity", "name": "Forager" },
              { "id": "profile_forager", "type": "agent", "entities": ["entity_forager"],
                "brain": "htn", "rootTask": "htn_forage",
                "htnInterrupt": ["THREAT_KNOWN", "CAN_SEE_ENEMY"],
                "hostileTags": ["player"],
                "sensors": [ { "kind": "visual", "range": 8, "fov": 360, "sensitivity": 0.6 } ],
                "walkSpeed": 2.0, "runSpeed": 4.0, "alertDecaySeconds": 2.0, "replanCooldown": 0.3 }
            ]
            """);
        WriteContent("herd.json", """
            [
              { "id": "role_watcher", "type": "role", "slots": 2, "condition": "ROLE_WATCHER", "ringRadius": 6 },
              { "id": "role_grazer", "type": "role", "slots": 99 },
              { "id": "group_herd", "type": "group",
                "roles": ["role_watcher", "role_grazer"],
                "staleness": { "threat_level": 3.0, "threat_position": 6.0 },
                "alertDecaySeconds": 4.0, "minMembers": 2, "maxMembers": 12,
                "passports": ["herd_beast"] },
              { "id": "collective_plains", "type": "collective", "budget": 24, "sites": [
                  { "position": [-30, 0, -30], "group": "group_herd",
                    "members": [ { "entity": "entity_beast", "count": 6 } ], "spawnRadius": 4 },
                  { "position": [-30, 0, 30], "group": "group_herd",
                    "members": [ { "entity": "entity_beast", "count": 4 } ], "spawnRadius": 4 } ] },
              { "id": "entity_beast", "type": "entity", "name": "Beast", "tags": ["beast"] },
              { "id": "profile_beast", "type": "agent", "entities": ["entity_beast"],
                "brain": "fsm", "fsmBrain": "fsmbrain_beast",
                "passport": "herd_beast", "hostileTags": ["player"],
                "sensors": [ { "kind": "visual", "range": 7, "fov": 360, "sensitivity": 0.6 } ],
                "walkSpeed": 1.4, "runSpeed": 3.8, "alertDecaySeconds": 2.0 },
              { "id": "fsmbrain_beast", "type": "fsmbrain", "initial": "graze", "states": {
                  "graze": {
                    "steering": { "type": "Wander", "speed": 1.0, "radius": 2, "interval": 1.5 },
                    "transitions": [
                      { "to": "flee", "when": [ { "type": "Any", "conditions": [
                          { "type": "AgentCondition", "condition": "GROUP_ALERT" },
                          { "type": "AgentCondition", "condition": "THREAT_KNOWN" },
                          { "type": "AgentCondition", "condition": "CAN_SEE_ENEMY" } ] } ] },
                      { "to": "watch", "when": [ { "type": "BeliefEquals", "key": "role", "value": "role_watcher" } ] } ] },
                  "watch": {
                    "steering": { "type": "MoveTo", "target": "post", "speed": 1.4 },
                    "transitions": [
                      { "to": "flee", "when": [ { "type": "Any", "conditions": [
                          { "type": "AgentCondition", "condition": "GROUP_ALERT" },
                          { "type": "AgentCondition", "condition": "THREAT_KNOWN" },
                          { "type": "AgentCondition", "condition": "CAN_SEE_ENEMY" } ] } ] },
                      { "to": "graze", "when": [ { "type": "Not", "condition":
                          { "type": "BeliefEquals", "key": "role", "value": "role_watcher" } } ] } ] },
                  "flee": {
                    "steering": { "type": "FleeFrom", "speed": 3.8, "distance": 8 },
                    "transitions": [
                      { "to": "graze", "when": [ { "type": "Not", "condition": { "type": "Any", "conditions": [
                          { "type": "AgentCondition", "condition": "GROUP_ALERT" },
                          { "type": "AgentCondition", "condition": "THREAT_KNOWN" },
                          { "type": "AgentCondition", "condition": "CAN_SEE_ENEMY" } ] } } ] } ] }
                } }
            ]
            """);
        WriteContent("metasensors.json", """
            { "id": "metasensor_poke", "type": "metasensor",
              "watch": "Player.Poked", "window": 5.0, "threshold": 3,
              "setCondition": "ANNOYED", "agentKey": "agentId" }
            """);
        WriteContent("lifecycle.json", """
            { "id": "lifecycle_test", "type": "lifecycle", "spawns": [] }
            """);
    }

    public (GameSession Session, RpgRuntime Rpg, AiRuntime Ai) CreateLoadedSession()
    {
        var session = GameSession.Create(Services, LatticeWorld.AddDefTypes(LatticeAi.CreateDefTypes()));
        var rpg = LatticeRpg.Attach(session);
        var narrative = LatticeNarrative.Attach(session, rpg);
        var ai = LatticeAi.Attach(session, rpg, narrative);
        LatticeWorld.Attach(session, rpg); // inert without time defs; M5 tests write them
        var report = session.LoadContent();
        Assert.True(report.Ok, string.Join("; ", report.Errors));
        session.Boot("lifecycle_test");
        session.Events.DispatchPending();
        return (session, rpg, ai);
    }

    /// <summary>Advance simulation + the animation stub together.</summary>
    public void TickSeconds(GameSession session, double seconds)
    {
        var ticks = (int)Math.Round(seconds * 30);
        for (var i = 0; i < ticks; i++)
        {
            session.AdvanceTick(1f / 30f);
            Animation.Advance(1.0 / 30.0);
        }
    }

    public void Dispose()
    {
        Content.Dispose();
        try
        {
            Directory.Delete(ContentRoot, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }
}
