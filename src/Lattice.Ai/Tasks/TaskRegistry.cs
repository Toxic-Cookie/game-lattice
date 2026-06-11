using System.Text.Json;
using Lattice.Ai.Agents;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Rpg.Effects;

namespace Lattice.Ai.Tasks;

public enum TaskStatus
{
    Running,
    Complete,
    Failed,
}

/// <summary>
/// One atomic behavior in the Half-Life vocabulary (ch02 §2.5). Tasks are
/// unblendable by design — an agent commits to one task at a time, and the
/// constraint produces decisive, believable behavior.
/// </summary>
public interface ITaskExecutor
{
    /// <summary>The JSON "task" discriminator.</summary>
    string Type { get; }

    /// <summary>Begin a run; the returned object is this run's private state.</summary>
    object? Start(AgentContext ctx, JsonElement args);

    TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt);

    void Validate(JsonElement args, EffectValidationContext context);
}

/// <summary>Registry of task executors keyed by the "task" discriminator.</summary>
public sealed class TaskRegistry
{
    private readonly Dictionary<string, ITaskExecutor> _byType = new(StringComparer.Ordinal);

    public void Register(ITaskExecutor executor) => _byType[executor.Type] = executor;

    /// <summary>Every registered executor, ordered by type (manifest exporter).</summary>
    public IEnumerable<ITaskExecutor> All => _byType.Values.OrderBy(e => e.Type, StringComparer.Ordinal);

    public bool TryGet(JsonElement taskElement, out ITaskExecutor executor, out string taskType)
    {
        taskType = JsonArgs.TryGetString(taskElement, "task", out var t) ? t : "?";
        return _byType.TryGetValue(taskType, out executor!);
    }

    public void ValidateList(IEnumerable<JsonElement>? tasks, string owner, DefRegistry registry, IFormulaEngine formulas, ContentLoadReport report)
    {
        foreach (var task in tasks ?? [])
        {
            var context = new EffectValidationContext { Registry = registry, Formulas = formulas, Report = report, Owner = owner };
            if (!JsonArgs.TryGetString(task, "task", out var type))
            {
                context.Error("task payload missing 'task'.");
                continue;
            }

            if (!_byType.TryGetValue(type, out var executor))
            {
                context.Error($"unknown task type '{type}'.");
                continue;
            }

            executor.Validate(task, context);
        }
    }

    public static TaskRegistry CreateDefault()
    {
        var registry = new TaskRegistry();
        registry.Register(new MoveToTask());
        registry.Register(new WaitTask());
        registry.Register(new PlayAnimationTask());
        registry.Register(new FaceEntityTask());
        registry.Register(new UseSmartObjectTask());
        registry.Register(new PublishEventTask());
        registry.Register(new SetConditionTask());
        registry.Register(new ClearConditionTask());
        registry.Register(new NextPatrolPointTask());
        registry.Register(new SelectNewScheduleTask());
        registry.Register(new Utility.PerformActivityTask());
        return registry;
    }
}
