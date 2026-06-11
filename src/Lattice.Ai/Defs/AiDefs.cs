using System.Text.Json;
using Lattice.Core.Content;

namespace Lattice.Ai.Defs;

/// <summary>
/// The data-declared condition catalog: names map to bit positions in
/// declaration order. One catalog per profile (default: conditions_default);
/// at most 32 names (validation enforces).
/// </summary>
public sealed class ConditionCatalogDef : Def
{
    public List<string> Names { get; set; } = [];
}

/// <summary>
/// An agent profile is the whole personality of an NPC type (plan/04 §11):
/// brain tier, sensor calibration, behavior content, movement. Tiered
/// brains are the governing rule (the F.E.A.R. rat problem, ch07 §7.1):
/// "fsm" for simple agents, "schedules" for deliberative ones; M4b–M4d add
/// "bt", "goap", and "htn".
/// </summary>
public sealed class AgentProfileDef : Def
{
    /// <summary>Entity template IDs this profile attaches to.</summary>
    public List<string> Entities { get; set; } = [];

    /// <summary>Brain tier: "fsm" or "schedules" (M4a), "bt" (M4b), "goap" (M4c).</summary>
    public string Brain { get; set; } = "fsm";

    /// <summary>FSM brain def (brain = "fsm").</summary>
    public string? FsmBrain { get; set; }

    /// <summary>Schedule def IDs in priority order, highest first (brain = "schedules").</summary>
    public List<string>? Schedules { get; set; }

    /// <summary>Behavior tree def (brain = "bt").</summary>
    public string? BehaviorTree { get; set; }

    /// <summary>Seconds between brain ticks; 0 = think every simulation tick (ch06 §6.9 tick-rate decoupling).</summary>
    public double ThinkInterval { get; set; }

    /// <summary>Need def IDs this agent tracks (decayed per tick; drive the utility selector).</summary>
    public List<string>? Needs { get; set; }

    /// <summary>Activity def IDs the PerformActivity task chooses among.</summary>
    public List<string>? Activities { get; set; }

    /// <summary>GOAP goal def IDs (brain = "goap").</summary>
    public List<string>? Goals { get; set; }

    /// <summary>GOAP action def IDs — the F.E.A.R. action-subset pattern (brain = "goap").</summary>
    public List<string>? Actions { get; set; }

    /// <summary>Cost profile def ID overriding action costs (personality as data).</summary>
    public string? CostProfile { get; set; }

    /// <summary>Beliefs seeded at spawn (e.g. {"weapon_loaded": true}).</summary>
    public Dictionary<string, JsonElement>? InitialBeliefs { get; set; }

    /// <summary>A new goal must beat the active one's priority by this margin (ch07 §7.4 oscillation guard).</summary>
    public double GoalHysteresis { get; set; } = 0.5;

    /// <summary>Minimum seconds between replans (ch07 anti-pattern 1).</summary>
    public double ReplanCooldown { get; set; } = 0.5;

    /// <summary>Root HTN task ID, compound or primitive (brain = "htn").</summary>
    public string? RootTask { get; set; }

    /// <summary>Condition names whose appearance forces an HTN re-decomposition.</summary>
    public List<string>? HtnInterrupt { get; set; }

    /// <summary>Group-compatibility tag for collective recycling (HZD Part 4).</summary>
    public string? Passport { get; set; }

    /// <summary>Meta-sensor def IDs watching player behavior patterns on behalf of this agent.</summary>
    public List<string>? MetaSensors { get; set; }

    /// <summary>Condition name → global flag key: truthy flags set the bit each update ("IS_NIGHT": "is_night").</summary>
    public Dictionary<string, string>? FlagConditions { get; set; }

    public List<SensorSpec>? Sensors { get; set; }

    /// <summary>Condition catalog def ID.</summary>
    public string Conditions { get; set; } = "conditions_default";

    /// <summary>Tags that mark entities as threats to this agent.</summary>
    public List<string>? HostileTags { get; set; }

    public double WalkSpeed { get; set; } = 2.0;

    public double RunSpeed { get; set; } = 4.0;

    /// <summary>Seconds without threat before Alert decays back to Idle.</summary>
    public double AlertDecaySeconds { get; set; } = 5.0;

    /// <summary>Patrol route waypoints ([x,y,z] each) for the patrol_point move target.</summary>
    public List<float[]>? PatrolPoints { get; set; }

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var entity in Entities)
        {
            yield return new DefReference(entity, $"{Id}.entities");
        }

        if (FsmBrain is not null)
        {
            yield return new DefReference(FsmBrain, $"{Id}.fsmBrain");
        }

        foreach (var schedule in Schedules ?? [])
        {
            yield return new DefReference(schedule, $"{Id}.schedules");
        }

        if (BehaviorTree is not null)
        {
            yield return new DefReference(BehaviorTree, $"{Id}.behaviorTree");
        }

        foreach (var need in Needs ?? [])
        {
            yield return new DefReference(need, $"{Id}.needs");
        }

        foreach (var activity in Activities ?? [])
        {
            yield return new DefReference(activity, $"{Id}.activities");
        }

        foreach (var goal in Goals ?? [])
        {
            yield return new DefReference(goal, $"{Id}.goals");
        }

        foreach (var action in Actions ?? [])
        {
            yield return new DefReference(action, $"{Id}.actions");
        }

        if (CostProfile is not null)
        {
            yield return new DefReference(CostProfile, $"{Id}.costProfile");
        }

        if (RootTask is not null)
        {
            yield return new DefReference(RootTask, $"{Id}.rootTask");
        }

        foreach (var sensor in MetaSensors ?? [])
        {
            yield return new DefReference(sensor, $"{Id}.metaSensors");
        }

        yield return new DefReference(Conditions, $"{Id}.conditions");
    }

    public sealed class SensorSpec
    {
        /// <summary>"visual", "auditory", "smell", or "proximity".</summary>
        public string Kind { get; set; } = "visual";

        public double Range { get; set; } = 10.0;

        /// <summary>0–1; gates perceived detail: ≥0.8 full, ≥0.4 partial, else minimal (ch05 §5.2).</summary>
        public double Sensitivity { get; set; } = 1.0;

        /// <summary>Field of view in degrees (visual only; 360 = no cone).</summary>
        public double Fov { get; set; } = 360.0;
    }
}

/// <summary>
/// Data-driven simple FSM brain — the rat tier (ch07 §7.1): states pair a
/// steering primitive with condition-gated transitions. ~10 lines of JSON
/// per critter, zero planner overhead.
/// </summary>
public sealed class FsmBrainDef : Def
{
    public string Initial { get; set; } = "";

    public Dictionary<string, BrainState> States { get; set; } = new(StringComparer.Ordinal);

    public sealed class BrainState
    {
        /// <summary>Steering primitive payload ({"type":"Wander","speed":1} etc.).</summary>
        public JsonElement? Steering { get; set; }

        public List<Transition>? Transitions { get; set; }
    }

    public sealed class Transition
    {
        public string To { get; set; } = "";

        /// <summary>Condition primitives (subject = the agent entity); all must hold.</summary>
        public List<JsonElement>? When { get; set; }
    }
}

/// <summary>
/// A Half-Life schedule (ch02 §2.5): a condition-gated macro-behavior.
/// Selection requires all <see cref="Require"/> conditions; any
/// <see cref="Interrupt"/> condition invalidates it mid-run. The
/// invalidation feedback loop is the reactivity — no event handlers.
/// </summary>
public sealed class ScheduleDef : Def
{
    /// <summary>Selection priority; higher wins among selectable schedules.</summary>
    public double Priority { get; set; }

    /// <summary>Meta states ("Idle", "Alert") in which this schedule is selectable; empty = any.</summary>
    public List<string>? MetaStates { get; set; }

    /// <summary>Condition names that must all be set to select this schedule.</summary>
    public List<string>? Require { get; set; }

    /// <summary>Condition names whose appearance invalidates the running schedule.</summary>
    public List<string>? Interrupt { get; set; }

    /// <summary>Ordered task payloads ({"task":"MoveTo",...}).</summary>
    public List<JsonElement> Tasks { get; set; } = [];
}
