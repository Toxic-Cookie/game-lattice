using Lattice.Ai.Defs;
using Lattice.Ai.Tasks;
using TaskStatus = Lattice.Ai.Tasks.TaskStatus;

namespace Lattice.Ai.Agents;

/// <summary>
/// The Half-Life deliberative brain (ch02 §2.5): condition-gated schedules
/// of atomic, unblendable tasks. The loop — sensors set conditions,
/// conditions invalidate the running schedule, a new schedule is selected —
/// is the reactivity; there are no event handlers. Every decision lands in
/// the agent trace.
/// </summary>
public sealed class ScheduleBrain : IBrain
{
    private ScheduleDef? _current;
    private uint _interruptMask;
    private int _taskIndex;
    private bool _taskStarted;
    private object? _taskState;

    public string Kind => "schedules";

    public string? CurrentScheduleId => _current?.Id;

    public int TaskIndex => _taskIndex;

    public string Describe()
        => _current is null ? "schedules (none selected)" : $"schedule {_current.Id} task {_taskIndex + 1}/{_current.Tasks.Count}";

    public void Tick(AgentContext ctx, float dt)
    {
        var agent = ctx.Agent;

        // 1. validity: any interrupt condition cancels the running schedule
        if (_current is not null && agent.Conditions.HasAnyOf(_interruptMask))
        {
            var triggered = agent.Conditions.SetNames(agent.Catalog)
                .Where(name => (agent.Catalog.MaskOf([name]) & _interruptMask) != 0);
            agent.AddTrace(ctx.Session.Tick, $"{_current.Id} invalidated by {string.Join("|", triggered)}");
            Abandon(ctx);
        }

        // 2. selection
        if (_current is null && !Select(ctx))
        {
            return;
        }

        // 3. execute the current task
        var schedule = _current!;
        if (_taskIndex >= schedule.Tasks.Count)
        {
            Finish(ctx, "complete");
            return;
        }

        var taskElement = schedule.Tasks[_taskIndex];
        if (!ctx.Ai.Tasks.TryGet(taskElement, out var executor, out var taskType))
        {
            agent.AddTrace(ctx.Session.Tick, $"{schedule.Id} task '{taskType}' unknown");
            Finish(ctx, "failed");
            return;
        }

        if (!_taskStarted)
        {
            _taskState = executor.Start(ctx, taskElement);
            _taskStarted = true;
        }

        switch (executor.Tick(ctx, taskElement, ref _taskState, dt))
        {
            case TaskStatus.Complete:
                _taskIndex++;
                _taskStarted = false;
                _taskState = null;
                if (taskType == "SelectNewSchedule" || _taskIndex >= schedule.Tasks.Count)
                {
                    Finish(ctx, "complete");
                }

                break;

            case TaskStatus.Failed:
                agent.AddTrace(ctx.Session.Tick, $"{schedule.Id} task {taskType} failed");
                Finish(ctx, "failed");
                break;
        }
    }

    /// <summary>Pick the highest-priority schedule whose meta-state and require-conditions allow it.</summary>
    private bool Select(AgentContext ctx)
    {
        var agent = ctx.Agent;
        ScheduleDef? best = null;
        foreach (var scheduleId in agent.Profile.Schedules ?? [])
        {
            if (!ctx.Session.Defs.TryGet<ScheduleDef>(scheduleId, out var candidate))
            {
                continue;
            }

            if (candidate.MetaStates is { Count: > 0 } gates && !gates.Contains(agent.Meta.ToString()))
            {
                continue;
            }

            if (!agent.Conditions.HasAllOf(agent.Catalog.MaskOf(candidate.Require)))
            {
                continue;
            }

            if (best is null || candidate.Priority > best.Priority)
            {
                best = candidate;
            }
        }

        if (best is null)
        {
            return false;
        }

        _current = best;
        _interruptMask = agent.Catalog.MaskOf(best.Interrupt);
        _taskIndex = 0;
        _taskStarted = false;
        _taskState = null;
        agent.AddTrace(ctx.Session.Tick, $"selected {best.Id}");
        return true;
    }

    private void Finish(AgentContext ctx, string outcome)
    {
        if (_current is not null)
        {
            ctx.Agent.AddTrace(ctx.Session.Tick, $"{_current.Id} {outcome}");
        }

        Abandon(ctx);
    }

    private void Abandon(AgentContext ctx)
    {
        ctx.Agent.StopMoving();
        _current = null;
        _interruptMask = 0;
        _taskIndex = 0;
        _taskStarted = false;
        _taskState = null;
    }
}
