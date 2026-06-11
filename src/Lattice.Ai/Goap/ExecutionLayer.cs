using System.Numerics;
using Lattice.Ai.Agents;
using Lattice.Rpg.Effects;

namespace Lattice.Ai.Goap;

public enum ExecutionStatus
{
    Running,
    Complete,
    Failed,
}

/// <summary>
/// F.E.A.R.'s insight (case study Part 4): every plannable behavior reduces
/// to <c>GoTo → Animate → UseSmartObject</c>. This layer drives one
/// candidate action through those states via the navigation, animation, and
/// interaction seams. M4d's HTN primitive tasks execute through the same
/// layer — build once.
///
/// Reservation protocol: smart-object steps reserve at activation (failure
/// = someone else got there first → the action fails → the brain replans —
/// which is exactly how flanking emerges from exclusion). The *brain* owns
/// releasing, because occupation legitimately outlives the action.
/// </summary>
public sealed class ExecutionLayer
{
    private enum Stage
    {
        Idle,
        GoTo,
        Animate,
    }

    private Stage _stage = Stage.Idle;
    private GoapCandidate? _current;
    private string? _animation;

    /// <summary>The smart-object instance currently reserved on behalf of the agent (released by the brain).</summary>
    public string? ReservedObjectId { get; private set; }

    /// <summary>Begin executing a candidate. Returns false when activation fails (no path, reservation denied).</summary>
    public bool Begin(AgentContext ctx, GoapCandidate candidate)
    {
        _current = candidate;
        _animation = null;
        Vector3? destination = null;

        if (candidate.SmartObjectId is { } objectId)
        {
            var interactions = ctx.Ai.Narrative?.Interactions;
            if (interactions is null
                || !ctx.Session.World.TryGet(objectId, out var target)
                || !interactions.TryReserve(target, ctx.Entity.InstanceId))
            {
                ctx.Agent.AddTrace(ctx.Session.Tick, $"exec {candidate.Id}: reservation denied");
                _current = null;
                return false;
            }

            ReleaseIfDifferent(ctx, objectId);
            ReservedObjectId = objectId;

            var binding = interactions.GetBinding(target);
            var offset = binding?.ApproachOffset is { Length: 3 } o ? new Vector3(o[0], o[1], o[2]) : Vector3.Zero;
            destination = target.Position + offset;
            _animation = binding?.Animation;
        }
        else if (candidate.Action?.MoveTo is { } moveTarget)
        {
            destination = MoveTargets.ResolveSpec(ctx, moveTarget);
            if (destination is null)
            {
                ctx.Agent.AddTrace(ctx.Session.Tick, $"exec {candidate.Id}: no move target");
                _current = null;
                return false;
            }
        }

        _animation ??= candidate.Action?.Animation;

        if (destination is { } point)
        {
            var speed = candidate.Action?.Speed == "run" ? ctx.Agent.Profile.RunSpeed : ctx.Agent.Profile.WalkSpeed;
            if (!ctx.Ai.RequestPath(ctx, point, speed))
            {
                ctx.Agent.AddTrace(ctx.Session.Tick, $"exec {candidate.Id}: unreachable");
                _current = null;
                return false;
            }

            _stage = Stage.GoTo;
            return true;
        }

        return StartAnimateOrFinish(ctx);
    }

    public ExecutionStatus Tick(AgentContext ctx, float dt)
    {
        switch (_stage)
        {
            case Stage.GoTo:
                if (ctx.Agent.HasArrived)
                {
                    return StartAnimateOrFinish(ctx) ? Status() : ExecutionStatus.Failed;
                }

                return ctx.Agent.IsMoving ? ExecutionStatus.Running : ExecutionStatus.Failed;

            case Stage.Animate:
                return ctx.Session.Services.Animation.IsComplete(ctx.Entity.InstanceId, _animation!)
                    ? Finish()
                    : ExecutionStatus.Running;

            default:
                return _current is null ? ExecutionStatus.Failed : ExecutionStatus.Complete;
        }

        ExecutionStatus Status() => _stage == Stage.Idle ? ExecutionStatus.Complete : ExecutionStatus.Running;
    }

    /// <summary>Stop whatever is in flight (the reservation stays until the brain releases it).</summary>
    public void Abort(AgentContext ctx)
    {
        if (_stage == Stage.GoTo)
        {
            ctx.Agent.StopMoving();
        }

        _stage = Stage.Idle;
        _current = null;
    }

    /// <summary>Release the held reservation (goal switch, plan switch away from the object).</summary>
    public void ReleaseReservation(AgentContext ctx)
    {
        if (ReservedObjectId is { } id)
        {
            if (ctx.Session.World.TryGet(id, out var target))
            {
                ctx.Ai.Narrative?.Interactions.Release(target, ctx.Entity.InstanceId);
            }

            ReservedObjectId = null;
        }
    }

    private void ReleaseIfDifferent(AgentContext ctx, string newObjectId)
    {
        if (ReservedObjectId is { } held && held != newObjectId
            && ctx.Session.World.TryGet(held, out var target))
        {
            ctx.Ai.Narrative?.Interactions.Release(target, ctx.Entity.InstanceId);
            ReservedObjectId = null;
        }
    }

    private bool StartAnimateOrFinish(AgentContext ctx)
    {
        if (_animation is { } anim)
        {
            var interruptible = _current?.Action is not { AnimationBlocking: true };
            ctx.Session.Services.Animation.Play(ctx.Entity.InstanceId, anim, interruptible);
            _stage = Stage.Animate;
        }
        else
        {
            Finish();
        }

        return true;
    }

    private ExecutionStatus Finish()
    {
        _stage = Stage.Idle;
        return ExecutionStatus.Complete;
    }
}
