using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Narrative;
using Lattice.Rpg;

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
              { "id": "entity_patron", "type": "entity", "name": "Patron", "tags": ["patron"], "stats": { "stat_con": 3 } }
            ]
            """);
        WriteContent("conditions.json", """
            { "id": "conditions_default", "type": "conditions",
              "names": ["CAN_SEE_ENEMY", "THREAT_KNOWN", "HEAR_SOUND", "SMELL_DETECTED", "CONTACT", "DAMAGED", "CUSTOM_FLAG"] }
            """);
        WriteContent("profiles.json", """
            [
              { "id": "profile_guard", "type": "agent", "entities": ["entity_guard"],
                "brain": "schedules",
                "schedules": ["schedule_combat", "schedule_investigate", "schedule_search", "schedule_patrol"],
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
        WriteContent("lifecycle.json", """
            { "id": "lifecycle_test", "type": "lifecycle", "spawns": [] }
            """);
    }

    public (GameSession Session, RpgRuntime Rpg, AiRuntime Ai) CreateLoadedSession()
    {
        var session = GameSession.Create(Services, LatticeAi.CreateDefTypes());
        var rpg = LatticeRpg.Attach(session);
        var narrative = LatticeNarrative.Attach(session, rpg);
        var ai = LatticeAi.Attach(session, rpg, narrative);
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
