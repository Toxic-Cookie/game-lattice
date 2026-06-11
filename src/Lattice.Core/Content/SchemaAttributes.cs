namespace Lattice.Core.Content;

/// <summary>
/// Marks a string (or string-collection) property as a cross-def ID
/// reference (plan/06 §2). The schema generator emits
/// <c>"x-lattice-ref": "&lt;kind&gt;"</c>, which drives editor tooling and
/// documents the link for LLMs. Multiple acceptable kinds join with '|'.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LatticeRefAttribute(string kind) : Attribute
{
    public string Kind { get; } = kind;
}

/// <summary>
/// Marks a JSON-payload property as a discriminated union over a primitive
/// vocabulary ("effect", "condition", or "task"). The schema generator emits
/// the discriminator with an enum of the registered primitive type names.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LatticeUnionAttribute(string kind) : Attribute
{
    /// <summary>"effect" | "condition" | "task".</summary>
    public string Kind { get; } = kind;
}
