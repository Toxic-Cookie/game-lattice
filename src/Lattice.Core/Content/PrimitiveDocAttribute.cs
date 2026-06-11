namespace Lattice.Core.Content;

/// <summary>
/// LLM/modder-facing documentation carried by every primitive executor
/// (effects, conditions, tasks — plan/06 §1). The manifest exporter reads
/// these off the registered executors, so the docs can never drift from
/// what is actually registered.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PrimitiveDocAttribute(string description, string args, string example) : Attribute
{
    /// <summary>One line: what the primitive does.</summary>
    public string Description { get; } = description;

    /// <summary>Argument signature ("name: meaning; name?: optional meaning").</summary>
    public string Args { get; } = args;

    /// <summary>A complete, valid JSON payload.</summary>
    public string Example { get; } = example;
}
