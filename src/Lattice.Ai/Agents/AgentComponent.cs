using System.Numerics;
using Lattice.Ai.Defs;
using Lattice.Ai.Perception;
using Lattice.Core.Simulation;

namespace Lattice.Ai.Agents;

/// <summary>Operational gate over schedule selection (Half-Life meta-state, ch02 §2.5 layer 1).</summary>
public enum MetaState
{
    Idle,
    Alert,
}

/// <summary>A brain decides; the agent system perceives and moves. One instance per agent (brains hold per-agent state).</summary>
public interface IBrain
{
    string Kind { get; }

    void Tick(AgentContext ctx, float dt);

    /// <summary>One-line state summary for the debug console.</summary>
    string Describe();
}

/// <summary>Everything a brain/task touches for one agent.</summary>
public sealed class AgentContext
{
    public required AiRuntime Ai { get; init; }

    public required Entity Entity { get; init; }

    public required AgentComponent Agent { get; init; }

    public GameSession Session => Ai.Session;
}

/// <summary>
/// Per-entity AI state: beliefs, conditions, brain, movement, and the
/// decision trace. Deliberately not saved — brains re-perceive and
/// re-decide from world-observable facts after load (plan/01 §5).
/// </summary>
public sealed class AgentComponent
{
    private readonly List<string> _trace = [];

    public AgentComponent(AgentProfileDef profile, ConditionCatalog catalog, IBrain brain)
    {
        Profile = profile;
        Catalog = catalog;
        Brain = brain;
    }

    public AgentProfileDef Profile { get; }

    public ConditionCatalog Catalog { get; }

    public IBrain Brain { get; }

    /// <summary>Rich belief store (sensor-fed facts: enemy_position, sound_position, ...).</summary>
    public WorldState Beliefs { get; } = new();

    /// <summary>Fast flags, rebuilt from sensors every update then OR'd with <see cref="ManualConditions"/>.</summary>
    public ConditionSet Conditions;

    /// <summary>Sticky bits set/cleared by tasks (SetCondition/ClearCondition); survive the per-frame sensor refresh.</summary>
    public uint ManualConditions;

    public MetaState Meta { get; set; } = MetaState.Idle;

    /// <summary>Sim time of the last threat-ish perception (drives Alert decay).</summary>
    public double LastThreatAt { get; set; } = double.NegativeInfinity;

    /// <summary>Sim time of the last damage taken (drives the DAMAGED condition).</summary>
    public double LastDamagedAt { get; set; } = double.NegativeInfinity;

    public Vector3 Facing { get; set; } = Vector3.UnitX;

    // ── movement (driven by tasks/steering, executed by the agent system) ──

    public List<Vector3> Path { get; } = [];

    public int PathIndex { get; set; }

    public double MoveSpeed { get; set; }

    public bool HasArrived { get; set; }

    public int PatrolIndex { get; set; }

    public bool IsMoving => PathIndex < Path.Count;

    public void SetPath(IEnumerable<Vector3> waypoints, double speed)
    {
        Path.Clear();
        Path.AddRange(waypoints);
        PathIndex = 0;
        MoveSpeed = speed;
        HasArrived = false;
    }

    public void StopMoving()
    {
        Path.Clear();
        PathIndex = 0;
    }

    // ── debug trace (ch07 §7.3: the decision log is built with the system) ──

    public IReadOnlyList<string> Trace => _trace;

    public void AddTrace(long tick, string message)
    {
        _trace.Add($"[{tick}] {message}");
        if (_trace.Count > 64)
        {
            _trace.RemoveAt(0);
        }
    }
}
