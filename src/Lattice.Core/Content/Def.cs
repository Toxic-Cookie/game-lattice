using System.Text.Json.Serialization;

namespace Lattice.Core.Content;

/// <summary>A cross-def reference, reported for the link pass (dangling-ID detection).</summary>
/// <param name="TargetId">The referenced def ID.</param>
/// <param name="Context">Where in the owning def the reference appears (for error messages).</param>
public readonly record struct DefReference(string TargetId, string Context);

/// <summary>
/// Base class for all content definitions. Defs are immutable once loaded —
/// the loader populates them via the serializer, the link pass validates
/// them, and from then on only the hot-reload manager may replace them.
/// Runtime state lives on instances that reference defs by ID.
/// </summary>
public abstract class Def
{
    /// <summary>Globally unique string ID, prefix-namespaced by convention (item_*, npc_*, ...).</summary>
    public string Id { get; set; } = "";

    /// <summary>One-line human/LLM description; surfaced by the manifest exporter (plan/06 §1).</summary>
    public string? Description { get; set; }

    /// <summary>Parent def ID for blueprint inheritance (plan/06 §4); resolved by the loader before deserialization.</summary>
    public string? Inherits { get; set; }

    /// <summary>Content-relative path of the file this def was loaded from (diagnostics + hot reload).</summary>
    [JsonIgnore]
    public string? SourceFile { get; internal set; }

    /// <summary>IDs of other defs this def references; checked by the link pass.</summary>
    public virtual IEnumerable<DefReference> GetReferences() => [];

    /// <summary>Formula strings this def carries; syntax-checked by validation.</summary>
    public virtual IEnumerable<string> GetFormulas() => [];
}
