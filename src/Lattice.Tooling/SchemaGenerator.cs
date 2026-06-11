using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lattice.Core.Content;

namespace Lattice.Tooling;

/// <summary>
/// Reflection-based JSON-schema emission for def kinds (plan/06 §2).
/// Schemas are generated from the C# def models so they can never drift
/// from the loader's actual shape; [LatticeRef] properties carry
/// <c>x-lattice-ref</c> annotations and [LatticeUnion] payload lists emit
/// discriminators with the registered primitive names. CI regenerates and
/// fails on diff.
/// </summary>
public static class SchemaGenerator
{
    /// <summary>Registered primitive type names per union kind ("effect"/"condition"/"task"), supplied by the caller.</summary>
    public static IReadOnlyDictionary<string, string[]> UnionVocabularies { get; set; }
        = new Dictionary<string, string[]>(StringComparer.Ordinal);

    public static string GenerateSchemaJson(string typeName, Type defType)
    {
        var properties = new JsonObject
        {
            ["id"] = new JsonObject { ["type"] = "string", ["description"] = "Globally unique def ID." },
            ["type"] = new JsonObject { ["const"] = typeName },
            ["description"] = new JsonObject { ["type"] = "string", ["description"] = "One line for humans and LLMs (surfaced by the manifest)." },
            ["inherits"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Parent def ID (same kind); objects deep-merge, scalars override, arrays replace or take {\"$append\"/\"$remove\"} patches.",
                ["x-lattice-ref"] = typeName,
            },
        };

        foreach (var prop in defType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null || !prop.CanWrite)
            {
                continue;
            }

            var jsonName = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            if (jsonName is "id" or "description" or "inherits")
            {
                continue; // already declared
            }

            properties[jsonName] = SchemaFor(prop.PropertyType, prop);
        }

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = $"https://lattice.dev/schemas/{typeName}.schema.json",
            ["title"] = defType.Name,
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray("id", "type"),
        };

        return schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>A combined schema accepting any def kind (or the {"$schema", "defs": [...]} wrapper) — for file-level headers.</summary>
    public static string GenerateCombinedSchemaJson(IEnumerable<string> typeNames)
    {
        var anyDef = new JsonObject
        {
            ["oneOf"] = new JsonArray(typeNames
                .OrderBy(n => n, StringComparer.Ordinal)
                .Select(n => (JsonNode)new JsonObject { ["$ref"] = $"./{n}.schema.json" })
                .ToArray()),
        };
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://lattice.dev/schemas/lattice.schema.json",
            ["title"] = "Lattice content file",
            ["oneOf"] = new JsonArray(
                anyDef.DeepClone(),
                new JsonObject { ["type"] = "array", ["items"] = anyDef.DeepClone() },
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["defs"] = new JsonObject { ["type"] = "array", ["items"] = anyDef.DeepClone() },
                    },
                    ["required"] = new JsonArray("defs"),
                }),
        };

        return schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject SchemaFor(Type type, PropertyInfo? prop = null)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var reference = prop?.GetCustomAttribute<LatticeRefAttribute>();
        var union = prop?.GetCustomAttribute<LatticeUnionAttribute>();

        if (underlying == typeof(string))
        {
            var node = new JsonObject { ["type"] = "string" };
            if (reference is not null)
            {
                node["x-lattice-ref"] = reference.Kind;
            }

            return node;
        }

        if (underlying == typeof(bool))
        {
            return new JsonObject { ["type"] = "boolean" };
        }

        if (underlying.IsPrimitive || underlying == typeof(decimal))
        {
            return new JsonObject { ["type"] = "number" };
        }

        if (underlying == typeof(JsonElement))
        {
            return union is not null ? UnionSchema(union.Kind) : new JsonObject();
        }

        if (underlying.IsArray)
        {
            return new JsonObject { ["type"] = "array", ["items"] = SchemaFor(underlying.GetElementType()!) };
        }

        if (underlying.IsGenericType)
        {
            var generic = underlying.GetGenericTypeDefinition();
            if (generic == typeof(List<>))
            {
                var element = underlying.GetGenericArguments()[0];
                JsonObject items;
                if (element == typeof(JsonElement) && union is not null)
                {
                    items = UnionSchema(union.Kind);
                }
                else if (element == typeof(string) && reference is not null)
                {
                    items = new JsonObject { ["type"] = "string", ["x-lattice-ref"] = reference.Kind };
                }
                else
                {
                    items = SchemaFor(element);
                }

                return new JsonObject { ["type"] = "array", ["items"] = items };
            }

            if (generic == typeof(Dictionary<,>))
            {
                return new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = SchemaFor(underlying.GetGenericArguments()[1]),
                };
            }
        }

        // nested object (e.g. LifecycleDef.SpawnEntry): recurse over its properties
        var nested = new JsonObject();
        foreach (var nestedProp in underlying.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (nestedProp.CanWrite)
            {
                nested[JsonNamingPolicy.CamelCase.ConvertName(nestedProp.Name)] = SchemaFor(nestedProp.PropertyType, nestedProp);
            }
        }

        return new JsonObject { ["type"] = "object", ["properties"] = nested };
    }

    /// <summary>A primitive payload: required discriminator with the registered names enumerated.</summary>
    private static JsonObject UnionSchema(string kind)
    {
        var discriminator = kind == "task" ? "task" : "type";
        var discriminatorSchema = new JsonObject { ["type"] = "string" };
        if (UnionVocabularies.TryGetValue(kind, out var names) && names.Length > 0)
        {
            discriminatorSchema["enum"] = new JsonArray(names.Select(n => (JsonNode)n).ToArray());
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["x-lattice-union"] = kind,
            ["description"] = $"A {kind} primitive payload; see the manifest for arg signatures.",
            ["properties"] = new JsonObject { [discriminator] = discriminatorSchema },
            ["required"] = new JsonArray(discriminator),
        };
    }
}
