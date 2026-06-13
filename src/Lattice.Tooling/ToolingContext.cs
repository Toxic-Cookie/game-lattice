using Lattice.Ai;
using Lattice.Ai.Tasks;
using Lattice.Ai.Utility;
using Lattice.Core.Content;
using Lattice.Narrative;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Effects;
using Lattice.World;

namespace Lattice.Tooling;

/// <summary>
/// The fully-assembled Lattice authoring context shared by every tooling
/// command (validate / manifest / schemas) and by Lattice.Studio: the
/// def-type registry plus the effect / condition / task primitive registries.
/// Built once from the same module wiring the runtime uses so tooling can
/// never diverge from the engine's actual def kinds and primitive vocabulary.
/// </summary>
public sealed class ToolingContext
{
    /// <summary>JSON <c>"type"</c> discriminator -> CLR def type, across every module.</summary>
    public required DefTypeRegistry Types { get; init; }

    /// <summary>Registered effect primitives (the <c>effect</c> union vocabulary).</summary>
    public required EffectRegistry Effects { get; init; }

    /// <summary>Registered condition primitives (the <c>condition</c> union vocabulary).</summary>
    public required ConditionRegistry Conditions { get; init; }

    /// <summary>Registered task primitives (the <c>task</c> union vocabulary).</summary>
    public required TaskRegistry Tasks { get; init; }

    /// <summary>Assemble the full context with every built-in module registered.</summary>
    public static ToolingContext Create()
    {
        var effects = BuiltinEffects.CreateDefault();
        effects.Register(new StartQuestEffect());

        var conditions = ConditionRegistry.CreateDefault();
        conditions.Register(new AgentConditionEvaluator());
        conditions.Register(new AgentMetaCondition());
        conditions.Register(new BeliefEqualsCondition());
        conditions.Register(new UtilityAtLeastCondition());
        conditions.Register(new NeedBelowCondition());

        return new ToolingContext
        {
            Types = LatticeWorld.AddDefTypes(LatticeAi.CreateDefTypes()),
            Effects = effects,
            Conditions = conditions,
            Tasks = TaskRegistry.CreateDefault(),
        };
    }
}
