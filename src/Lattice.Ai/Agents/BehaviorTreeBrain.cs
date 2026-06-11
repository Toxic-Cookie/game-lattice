using System.Text;
using System.Text.Json;
using Lattice.Ai.Defs;
using Lattice.Core.Content;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Effects;
using TaskStatus = Lattice.Ai.Tasks.TaskStatus;

namespace Lattice.Ai.Agents;

/// <summary>
/// The industry-default mid-tier brain (ch05 §5.5). Each agent gets its own
/// node-tree instance (nodes hold per-agent run state). The tree is ticked
/// from the root every think; when the root finishes — either way — it
/// restarts on the next think. Sequences resume their running child;
/// Selectors are reactive (higher-priority children re-evaluated every tick,
/// preempting a running lower one); ConditionGate decorators abort their
/// running subtree the moment the gate fails.
/// </summary>
public sealed class BehaviorTreeBrain : IBrain
{
    private readonly BehaviorTreeDef _def;
    private readonly BtNode _root;

    public BehaviorTreeBrain(BehaviorTreeDef def, DefRegistry defs)
    {
        _def = def;
        _root = BtNode.Build(def.Root, defs, new HashSet<string>(StringComparer.Ordinal) { def.Id });
    }

    public string Kind => "bt";

    public void Tick(AgentContext ctx, float dt)
    {
        if (_root.Tick(ctx, dt) != TaskStatus.Running)
        {
            _root.Reset(ctx); // restart from the top next think
        }
    }

    public string Describe() => $"bt {_def.Id} root={_root.LastStatus?.ToString() ?? "-"}";

    /// <summary>Indented tree dump with each node's last-tick status (ch07: debug ships with the system).</summary>
    public string DescribeTree()
    {
        var builder = new StringBuilder();
        _root.Describe(builder, 0);
        return builder.ToString().TrimEnd();
    }
}

/// <summary>One behavior-tree node instance. Invariant: a node that returns non-Running has already cleared its own run state.</summary>
internal abstract class BtNode(string label)
{
    public string Label { get; } = label;

    /// <summary>Status of the most recent tick that reached this node (debug dump).</summary>
    public TaskStatus? LastStatus { get; private set; }

    public TaskStatus Tick(AgentContext ctx, float dt)
    {
        var status = OnTick(ctx, dt);
        LastStatus = status;
        return status;
    }

    protected abstract TaskStatus OnTick(AgentContext ctx, float dt);

    /// <summary>Clear run state (aborting any running descendant); keeps LastStatus for the dump.</summary>
    public virtual void Reset(AgentContext ctx)
    {
    }

    protected virtual IEnumerable<BtNode> Children => [];

    public void Describe(StringBuilder builder, int depth)
    {
        var marker = LastStatus switch
        {
            TaskStatus.Running => "…",
            TaskStatus.Complete => "✓",
            TaskStatus.Failed => "✗",
            _ => "·",
        };
        builder.Append(' ', depth * 2).Append(marker).Append(' ').AppendLine(Label);
        foreach (var child in Children)
        {
            child.Describe(builder, depth + 1);
        }
    }

    /// <summary>
    /// Build a node tree from JSON. <paramref name="path"/> carries the chain
    /// of subtree IDs currently being expanded: a repeat means a reference
    /// cycle, which degrades to an always-fail leaf (validation reports it at
    /// authoring time; the runtime must not recurse forever).
    /// </summary>
    public static BtNode Build(JsonElement node, DefRegistry defs, HashSet<string> path)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return new FailLeaf("(invalid node)");
        }

        if (node.TryGetProperty("task", out _))
        {
            return new TaskLeaf(node);
        }

        if (node.TryGetProperty("condition", out var condition))
        {
            return new ConditionLeaf(condition);
        }

        if (node.TryGetProperty("subtree", out var subtreeProp) && subtreeProp.ValueKind == JsonValueKind.String)
        {
            var subtreeId = subtreeProp.GetString()!;
            if (!path.Add(subtreeId))
            {
                return new FailLeaf($"(subtree cycle '{subtreeId}')");
            }

            BtNode inner = defs.TryGet<BehaviorTreeDef>(subtreeId, out var subtree)
                ? Build(subtree.Root, defs, path)
                : new FailLeaf($"(missing subtree '{subtreeId}')");
            path.Remove(subtreeId);
            return new SubtreeNode(subtreeId, inner);
        }

        var kind = JsonArgs.TryGetString(node, "node", out var k) ? k : "?";
        return kind switch
        {
            "Sequence" => new SequenceNode(BuildChildren(node, defs, path)),
            "Selector" => new SelectorNode(BuildChildren(node, defs, path)),
            "Inverter" => new InverterNode(BuildChild(node, defs, path)),
            "RepeatUntilFail" => new RepeatUntilFailNode(BuildChild(node, defs, path)),
            "Cooldown" => new CooldownNode(JsonArgs.GetDouble(node, "seconds", 1.0), BuildChild(node, defs, path)),
            "ConditionGate" => new ConditionGateNode(GetWhen(node), BuildChild(node, defs, path)),
            _ => new FailLeaf($"(unknown node '{kind}')"),
        };
    }

    private static List<BtNode> BuildChildren(JsonElement node, DefRegistry defs, HashSet<string> path)
        => node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array
            ? children.EnumerateArray().Select(c => Build(c, defs, path)).ToList()
            : [];

    private static BtNode BuildChild(JsonElement node, DefRegistry defs, HashSet<string> path)
        => node.TryGetProperty("child", out var child) ? Build(child, defs, path) : new FailLeaf("(missing child)");

    private static List<JsonElement> GetWhen(JsonElement node)
        => node.TryGetProperty("when", out var when) && when.ValueKind == JsonValueKind.Array
            ? when.EnumerateArray().ToList()
            : [];
}

/// <summary>Children in order; Failed stops it, Complete advances; resumes the running child between ticks.</summary>
internal sealed class SequenceNode(List<BtNode> children) : BtNode("Sequence")
{
    private int _index;

    protected override IEnumerable<BtNode> Children => children;

    protected override TaskStatus OnTick(AgentContext ctx, float dt)
    {
        while (_index < children.Count)
        {
            switch (children[_index].Tick(ctx, dt))
            {
                case TaskStatus.Running:
                    return TaskStatus.Running;
                case TaskStatus.Failed:
                    _index = 0;
                    return TaskStatus.Failed;
                default:
                    _index++;
                    break;
            }
        }

        _index = 0;
        return TaskStatus.Complete;
    }

    public override void Reset(AgentContext ctx)
    {
        if (_index < children.Count)
        {
            children[_index].Reset(ctx);
        }

        _index = 0;
    }
}

/// <summary>
/// Reactive selector: children are re-evaluated from the top in priority
/// order every tick, so a higher-priority branch that becomes viable
/// preempts (aborts) a lower-priority running one — this is how a patron
/// mid-drink still notices the threat branch above it. Keep side-effecting
/// task leaves behind ConditionGates in high-priority branches.
/// </summary>
internal sealed class SelectorNode(List<BtNode> children) : BtNode("Selector")
{
    private int _runningIndex = -1;

    protected override IEnumerable<BtNode> Children => children;

    protected override TaskStatus OnTick(AgentContext ctx, float dt)
    {
        for (var i = 0; i < children.Count; i++)
        {
            var status = children[i].Tick(ctx, dt);
            if (status == TaskStatus.Failed)
            {
                if (i == _runningIndex)
                {
                    _runningIndex = -1; // natural failure, not a preemption
                }

                continue;
            }

            if (_runningIndex >= 0 && _runningIndex != i)
            {
                // preemption: abort the losing branch, then re-run the winner
                // from a clean slate — its probe tick ran *before* the abort,
                // so shared agent state it set (e.g. a path) was just cleared
                ctx.Agent.AddTrace(ctx.Session.Tick, "bt branch preempted by higher-priority sibling");
                children[_runningIndex].Reset(ctx);
                _runningIndex = -1;
                children[i].Reset(ctx);
                status = children[i].Tick(ctx, dt);
                if (status == TaskStatus.Failed)
                {
                    continue;
                }
            }

            _runningIndex = status == TaskStatus.Running ? i : -1;
            return status;
        }

        _runningIndex = -1;
        return TaskStatus.Failed;
    }

    public override void Reset(AgentContext ctx)
    {
        if (_runningIndex >= 0)
        {
            children[_runningIndex].Reset(ctx);
        }

        _runningIndex = -1;
    }
}

internal sealed class InverterNode(BtNode child) : BtNode("Inverter")
{
    protected override IEnumerable<BtNode> Children => [child];

    protected override TaskStatus OnTick(AgentContext ctx, float dt)
        => child.Tick(ctx, dt) switch
        {
            TaskStatus.Running => TaskStatus.Running,
            TaskStatus.Complete => TaskStatus.Failed,
            _ => TaskStatus.Complete,
        };

    public override void Reset(AgentContext ctx) => child.Reset(ctx);
}

/// <summary>Re-runs its child until the child fails, then completes; runs across ticks.</summary>
internal sealed class RepeatUntilFailNode(BtNode child) : BtNode("RepeatUntilFail")
{
    protected override IEnumerable<BtNode> Children => [child];

    protected override TaskStatus OnTick(AgentContext ctx, float dt)
        => child.Tick(ctx, dt) == TaskStatus.Failed ? TaskStatus.Complete : TaskStatus.Running;

    public override void Reset(AgentContext ctx) => child.Reset(ctx);
}

/// <summary>Fails while cooling down; the cooldown starts whenever the child finishes (either way — prevents retry spam too).</summary>
internal sealed class CooldownNode(double seconds, BtNode child) : BtNode($"Cooldown {seconds:0.##}s")
{
    private double _readyAt;
    private bool _childRunning;

    protected override IEnumerable<BtNode> Children => [child];

    protected override TaskStatus OnTick(AgentContext ctx, float dt)
    {
        if (!_childRunning && ctx.Session.SimTimeSeconds < _readyAt)
        {
            return TaskStatus.Failed;
        }

        var status = child.Tick(ctx, dt);
        _childRunning = status == TaskStatus.Running;
        if (status != TaskStatus.Running)
        {
            _readyAt = ctx.Session.SimTimeSeconds + seconds;
        }

        return status;
    }

    public override void Reset(AgentContext ctx)
    {
        _childRunning = false;
        child.Reset(ctx); // an abort does not start the cooldown
    }
}

/// <summary>
/// Re-evaluates its conditions every tick; a failing gate aborts the running
/// subtree — this is where BT reactivity lives (the interrupt-mask analog).
/// </summary>
internal sealed class ConditionGateNode(List<JsonElement> when, BtNode child) : BtNode("ConditionGate")
{
    private bool _childRunning;

    protected override IEnumerable<BtNode> Children => [child];

    protected override TaskStatus OnTick(AgentContext ctx, float dt)
    {
        var conditionContext = new ConditionContext
        {
            Session = ctx.Session,
            Rpg = ctx.Ai.Rpg,
            Subject = ctx.Entity,
        };
        if (!ctx.Ai.Rpg.Conditions.EvaluateAll(when, conditionContext))
        {
            if (_childRunning)
            {
                ctx.Agent.AddTrace(ctx.Session.Tick, "bt gate failed; subtree aborted");
                child.Reset(ctx);
                _childRunning = false;
            }

            return TaskStatus.Failed;
        }

        var status = child.Tick(ctx, dt);
        _childRunning = status == TaskStatus.Running;
        return status;
    }

    public override void Reset(AgentContext ctx)
    {
        _childRunning = false;
        child.Reset(ctx);
    }
}

/// <summary>An expanded subtree reference (purely structural — keeps the dump readable).</summary>
internal sealed class SubtreeNode(string subtreeId, BtNode inner) : BtNode($"Subtree {subtreeId}")
{
    protected override IEnumerable<BtNode> Children => [inner];

    protected override TaskStatus OnTick(AgentContext ctx, float dt) => inner.Tick(ctx, dt);

    public override void Reset(AgentContext ctx) => inner.Reset(ctx);
}

/// <summary>A task-primitive leaf — the same executors schedules use.</summary>
internal sealed class TaskLeaf : BtNode
{
    private readonly JsonElement _element;
    private bool _started;
    private object? _state;

    public TaskLeaf(JsonElement element)
        : base(JsonArgs.TryGetString(element, "task", out var type) ? type : "(task?)")
        => _element = element;

    protected override TaskStatus OnTick(AgentContext ctx, float dt)
    {
        if (!ctx.Ai.Tasks.TryGet(_element, out var executor, out _))
        {
            return TaskStatus.Failed;
        }

        if (!_started)
        {
            _state = executor.Start(ctx, _element);
            _started = true;
        }

        var status = executor.Tick(ctx, _element, ref _state, dt);
        if (status != TaskStatus.Running)
        {
            _started = false;
            _state = null;
        }

        return status;
    }

    public override void Reset(AgentContext ctx)
    {
        if (_started)
        {
            ctx.Agent.StopMoving(); // aborted mid-task; don't keep walking
        }

        _started = false;
        _state = null;
    }
}

/// <summary>A condition-primitive leaf: Complete when all hold, else Failed. Accepts a single condition object or an array.</summary>
internal sealed class ConditionLeaf : BtNode
{
    private readonly List<JsonElement> _conditions;

    public ConditionLeaf(JsonElement condition)
        : base("Condition")
        => _conditions = condition.ValueKind == JsonValueKind.Array ? [.. condition.EnumerateArray()] : [condition];

    protected override TaskStatus OnTick(AgentContext ctx, float dt)
    {
        var conditionContext = new ConditionContext
        {
            Session = ctx.Session,
            Rpg = ctx.Ai.Rpg,
            Subject = ctx.Entity,
        };
        return ctx.Ai.Rpg.Conditions.EvaluateAll(_conditions, conditionContext) ? TaskStatus.Complete : TaskStatus.Failed;
    }
}

/// <summary>Fail-closed placeholder for malformed/unresolvable nodes (validation reports the cause).</summary>
internal sealed class FailLeaf(string label) : BtNode(label)
{
    protected override TaskStatus OnTick(AgentContext ctx, float dt) => TaskStatus.Failed;
}
