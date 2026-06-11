using System.Numerics;
using System.Text.Json;
using Lattice.Ai.Agents;
using Lattice.Core.Content;
using Lattice.Core.Events;
using Lattice.Rpg.Effects;

namespace Lattice.Ai.Tasks;

/// <summary>Navigate to a symbolic or literal target; completes on arrival.</summary>
[PrimitiveDoc("Walk/run to a target; completes on arrival, fails when unreachable.",
    "target: \"patrol_point\"|\"enemy\"|\"threat\"|\"sound\"|\"scent\"|\"last_enemy\"|\"spawn\"|\"post\"|\"group_threat\"|[x,y,z]; speed?: \"walk\"|\"run\"|number",
    """{"task":"MoveTo","target":"enemy","speed":"run"}""")]
internal sealed class MoveToTask : ITaskExecutor
{
    private static readonly object FailedMarker = new();

    public string Type => "MoveTo";

    public object? Start(AgentContext ctx, JsonElement args)
    {
        var target = MoveTargets.Resolve(ctx, args, "target");
        if (target is not { } destination)
        {
            return FailedMarker;
        }

        var speed = JsonArgs.TryGetString(args, "speed", out var s)
            ? s == "run" ? ctx.Agent.Profile.RunSpeed : ctx.Agent.Profile.WalkSpeed
            : JsonArgs.GetDouble(args, "speed", ctx.Agent.Profile.WalkSpeed);
        if (!ctx.Ai.RequestPath(ctx, destination, speed))
        {
            return FailedMarker;
        }

        return null;
    }

    public TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt)
    {
        if (ReferenceEquals(state, FailedMarker))
        {
            return TaskStatus.Failed;
        }

        if (ctx.Agent.HasArrived)
        {
            return TaskStatus.Complete;
        }

        return ctx.Agent.IsMoving ? TaskStatus.Running : TaskStatus.Failed;
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!args.TryGetProperty("target", out _))
        {
            v.Error("MoveTo requires a 'target'.");
        }
    }
}

/// <summary>Wait a fixed number of simulation seconds.</summary>
[PrimitiveDoc("Stand still for a number of simulated seconds.",
    "seconds: duration (default 1)",
    """{"task":"Wait","seconds":2.5}""")]
internal sealed class WaitTask : ITaskExecutor
{
    public string Type => "Wait";

    public object? Start(AgentContext ctx, JsonElement args) => 0.0;

    public TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt)
    {
        var elapsed = (double)state! + dt;
        state = elapsed;
        return elapsed >= JsonArgs.GetDouble(args, "seconds", 1.0) ? TaskStatus.Complete : TaskStatus.Running;
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (JsonArgs.GetDouble(args, "seconds", 1.0) <= 0)
        {
            v.Error("Wait 'seconds' must be positive.");
        }
    }
}

/// <summary>Play an animation through the host seam; completes when the host reports completion.</summary>
[PrimitiveDoc("Play an animation; completes when the host reports it finished. blocking animations suppress replanning.",
    "anim: animation id; blocking?: bool (default true = non-interruptible)",
    """{"task":"PlayAnimation","anim":"attack"}""")]
internal sealed class PlayAnimationTask : ITaskExecutor
{
    public string Type => "PlayAnimation";

    public object? Start(AgentContext ctx, JsonElement args)
    {
        var anim = JsonArgs.GetString(args, "anim");
        var blocking = !args.TryGetProperty("blocking", out var b) || b.ValueKind != JsonValueKind.False;
        ctx.Session.Services.Animation.Play(ctx.Entity.InstanceId, anim, interruptible: !blocking);
        return null;
    }

    public TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt)
    {
        var anim = JsonArgs.GetString(args, "anim");
        return ctx.Session.Services.Animation.IsComplete(ctx.Entity.InstanceId, anim)
            ? TaskStatus.Complete
            : TaskStatus.Running;
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!JsonArgs.TryGetString(args, "anim", out _))
        {
            v.Error("PlayAnimation requires 'anim'.");
        }
    }
}

/// <summary>Turn to face a symbolic target; instant.</summary>
[PrimitiveDoc("Turn to face a target instantly.",
    "target: same specs as MoveTo",
    """{"task":"FaceEntity","target":"enemy"}""")]
internal sealed class FaceEntityTask : ITaskExecutor
{
    public string Type => "FaceEntity";

    public object? Start(AgentContext ctx, JsonElement args) => null;

    public TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt)
    {
        if (MoveTargets.Resolve(ctx, args, "target") is { } target)
        {
            var direction = target - ctx.Entity.Position;
            if (direction.LengthSquared() > 0.0001f)
            {
                ctx.Agent.Facing = Vector3.Normalize(direction);
            }
        }

        return TaskStatus.Complete;
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!args.TryGetProperty("target", out _))
        {
            v.Error("FaceEntity requires a 'target'.");
        }
    }
}

/// <summary>Perform a verb on the nearest smart object within range (requires the Narrative module).</summary>
[PrimitiveDoc("Perform a verb on the nearest smart object within range.",
    "verb?: interaction verb (default \"interact\"); range?: world units (default 2)",
    """{"task":"UseSmartObject","verb":"open","range":2}""")]
internal sealed class UseSmartObjectTask : ITaskExecutor
{
    public string Type => "UseSmartObject";

    public object? Start(AgentContext ctx, JsonElement args) => null;

    public TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt)
    {
        var interactions = ctx.Ai.Narrative?.Interactions;
        if (interactions is null)
        {
            return TaskStatus.Failed;
        }

        var range = JsonArgs.GetDouble(args, "range", 2.0);
        var verb = JsonArgs.TryGetString(args, "verb", out var v) ? v : "interact";
        var target = ctx.Ai.QueryEntitiesNear(ctx.Entity, range)
            .FirstOrDefault(e => interactions.GetBinding(e) is not null);
        if (target is null)
        {
            return TaskStatus.Failed;
        }

        return interactions.TryInteract(ctx.Entity, target, verb, out _) ? TaskStatus.Complete : TaskStatus.Failed;
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
    }
}

/// <summary>Publish a bus event (scalar payload entries + agentId).</summary>
[PrimitiveDoc("Publish a bus event carrying the agent's instance id.",
    "event: topic",
    """{"task":"PublishEvent","event":"Npc.Waved"}""")]
internal sealed class PublishEventTask : ITaskExecutor
{
    public string Type => "PublishEvent";

    public object? Start(AgentContext ctx, JsonElement args) => null;

    public TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt)
    {
        ctx.Session.Events.Publish(
            JsonArgs.GetString(args, "event"),
            EventPayload.Of(("agentId", ctx.Entity.InstanceId)),
            ctx.Session.Tick);
        return TaskStatus.Complete;
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!JsonArgs.TryGetString(args, "event", out _))
        {
            v.Error("PublishEvent requires 'event'.");
        }
    }
}

/// <summary>Set a sticky manual condition bit (survives the per-frame sensor refresh).</summary>
[PrimitiveDoc("Set a sticky condition bit on the agent (survives sensor refresh; clear with ClearCondition).",
    "condition: catalog condition name",
    """{"task":"SetCondition","condition":"CUSTOM_FLAG"}""")]
internal sealed class SetConditionTask : ITaskExecutor
{
    public string Type => "SetCondition";

    public object? Start(AgentContext ctx, JsonElement args) => null;

    public TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt)
    {
        var name = JsonArgs.GetString(args, "condition");
        ctx.Agent.ManualConditions |= ctx.Agent.Catalog.MaskOf([name]);
        ctx.Agent.Conditions.Or(ctx.Agent.ManualConditions);
        return TaskStatus.Complete;
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!JsonArgs.TryGetString(args, "condition", out _))
        {
            v.Error("SetCondition requires 'condition'.");
        }
    }
}

/// <summary>Clear a sticky manual condition bit.</summary>
[PrimitiveDoc("Clear a sticky condition bit set by SetCondition.",
    "condition: catalog condition name",
    """{"task":"ClearCondition","condition":"CUSTOM_FLAG"}""")]
internal sealed class ClearConditionTask : ITaskExecutor
{
    public string Type => "ClearCondition";

    public object? Start(AgentContext ctx, JsonElement args) => null;

    public TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt)
    {
        var name = JsonArgs.GetString(args, "condition");
        ctx.Agent.ManualConditions &= ~ctx.Agent.Catalog.MaskOf([name]);
        return TaskStatus.Complete;
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!JsonArgs.TryGetString(args, "condition", out _))
        {
            v.Error("ClearCondition requires 'condition'.");
        }
    }
}

/// <summary>Advance the agent's patrol-route index.</summary>
[PrimitiveDoc("Advance to the next patrol waypoint (wraps around the profile's patrolPoints).",
    "(no args)",
    """{"task":"NextPatrolPoint"}""")]
internal sealed class NextPatrolPointTask : ITaskExecutor
{
    public string Type => "NextPatrolPoint";

    public object? Start(AgentContext ctx, JsonElement args) => null;

    public TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt)
    {
        var count = ctx.Agent.Profile.PatrolPoints?.Count ?? 0;
        if (count > 0)
        {
            ctx.Agent.PatrolIndex = (ctx.Agent.PatrolIndex + 1) % count;
        }

        return TaskStatus.Complete;
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
    }
}

/// <summary>Ends the schedule so the brain re-selects (the Half-Life loop-closer).</summary>
[PrimitiveDoc("End the current schedule so the brain re-selects (put it last in looping schedules).",
    "(no args)",
    """{"task":"SelectNewSchedule"}""")]
internal sealed class SelectNewScheduleTask : ITaskExecutor
{
    public string Type => "SelectNewSchedule";

    public object? Start(AgentContext ctx, JsonElement args) => null;

    public TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt) => TaskStatus.Complete;

    public void Validate(JsonElement args, EffectValidationContext v)
    {
    }
}
