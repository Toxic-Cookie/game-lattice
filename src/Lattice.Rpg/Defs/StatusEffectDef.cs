using System.Text.Json;
using System.Text.Json.Serialization;
using Lattice.Core.Content;

namespace Lattice.Rpg.Defs;

/// <summary>
/// Data-driven status effect (plan/02 §2): a duration, a stacking policy,
/// and a list of logic primitives (modifiers, periodic effects, tags).
/// </summary>
public sealed class StatusEffectDef : Def
{
    public string? Name { get; set; }

    /// <summary>Seconds the effect lasts; 0 or negative = permanent until removed.</summary>
    public double Duration { get; set; }

    /// <summary>"refresh" (reset duration), "stack" (add stacks up to maxStacks), or "ignore".</summary>
    public string Stacking { get; set; } = "refresh";

    public int MaxStacks { get; set; } = 99;

    public List<StatusLogicEntry>? Logic { get; set; }

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var entry in Logic ?? [])
        {
            switch (entry)
            {
                case FlatModifierEntry flat:
                    yield return new DefReference(flat.Stat, $"{Id}.logic");
                    break;
                case PercentModifierEntry pct:
                    yield return new DefReference(pct.Stat, $"{Id}.logic");
                    break;
            }
        }
    }
}

/// <summary>
/// One status-logic primitive, discriminated by <c>"type"</c>:
/// FlatModifier, PercentModifier, PeriodicEffect, or TagModifier.
/// Also reused for item <c>equipEffects</c> (where PeriodicEffect is invalid).
/// </summary>
[JsonConverter(typeof(StatusLogicEntryConverter))]
public abstract class StatusLogicEntry
{
}

/// <summary>Additive stat modifier: current = (base + Σflat) * (1 + Σpercent/100), clamped.</summary>
public sealed class FlatModifierEntry : StatusLogicEntry
{
    /// <summary>Stat def ID (e.g. "stat_str").</summary>
    public string Stat { get; set; } = "";

    public double Amount { get; set; }
}

/// <summary>Multiplicative stat modifier; percent 10 = +10%.</summary>
public sealed class PercentModifierEntry : StatusLogicEntry
{
    /// <summary>Stat def ID.</summary>
    public string Stat { get; set; } = "";

    public double Percent { get; set; }
}

/// <summary>Effects fired every <see cref="Interval"/> seconds (e.g. poison damage).</summary>
public sealed class PeriodicEffectEntry : StatusLogicEntry
{
    public double Interval { get; set; } = 1.0;

    public List<JsonElement> Effects { get; set; } = [];
}

/// <summary>Tags granted while the effect (or equipped item) is active.</summary>
public sealed class TagModifierEntry : StatusLogicEntry
{
    public List<string> AddTags { get; set; } = [];
}

/// <summary>Reads the "type" discriminator and dispatches to the concrete entry type.</summary>
public sealed class StatusLogicEntryConverter : JsonConverter<StatusLogicEntry>
{
    public override StatusLogicEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var element = doc.RootElement;
        if (!element.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Status logic entry is missing the 'type' discriminator.");
        }

        var typeName = typeProp.GetString();
        return typeName switch
        {
            "FlatModifier" => element.Deserialize<FlatModifierEntry>(options)!,
            "PercentModifier" => element.Deserialize<PercentModifierEntry>(options)!,
            "PeriodicEffect" => element.Deserialize<PeriodicEffectEntry>(options)!,
            "TagModifier" => element.Deserialize<TagModifierEntry>(options)!,
            _ => throw new JsonException($"Unknown status logic type '{typeName}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, StatusLogicEntry value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, value.GetType(), options);
}
