using System.Text.Json;

namespace Lattice.Core.Content;

/// <summary>
/// JSON-defined initialization: which flags the world starts with and what
/// gets spawned (plan/01 §3). <c>startingInventory</c> joins the schema in M2.
/// </summary>
public sealed class LifecycleDef : Def
{
    public string? InitialScene { get; set; }

    /// <summary>Initial global blackboard flags (bool/number/string values).</summary>
    public Dictionary<string, JsonElement>? GlobalFlags { get; set; }

    public List<SpawnEntry>? Spawns { get; set; }

    public override IEnumerable<DefReference> GetReferences()
    {
        if (Spawns is null)
        {
            yield break;
        }

        foreach (var spawn in Spawns)
        {
            yield return new DefReference(spawn.Entity, $"{Id}.spawns");
        }
    }

    public sealed class SpawnEntry
    {
        public string Entity { get; set; } = "";

        public int Count { get; set; } = 1;

        /// <summary>Optional [x, y, z] spawn position.</summary>
        public float[]? Position { get; set; }
    }
}

/// <summary>
/// Minimal entity template (M1): name, tags, and a flat numeric stat block.
/// The full stat system (StatDef, min/max/derived formulas) replaces the
/// plain stat map in M2; instances copy these values at spawn.
/// </summary>
public sealed class EntityTemplateDef : Def
{
    public string? Name { get; set; }

    public List<string>? Tags { get; set; }

    public Dictionary<string, double>? Stats { get; set; }
}
