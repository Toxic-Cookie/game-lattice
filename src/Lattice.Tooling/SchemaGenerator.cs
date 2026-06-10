using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Lattice.Tooling;

/// <summary>
/// Reflection-based JSON-schema emission for def kinds — M1 groundwork for
/// the full generator (plan/06 §2: discriminated unions for primitive
/// payloads, x-lattice-ref annotations). Schemas are generated from the C#
/// def models so they can never drift from the loader's actual shape.
/// </summary>
public static class SchemaGenerator
{
    public static string GenerateSchemaJson(string typeName, Type defType)
    {
        var properties = new JsonObject
        {
            ["id"] = new JsonObject { ["type"] = "string", ["description"] = "Globally unique def ID." },
            ["type"] = new JsonObject { ["const"] = typeName },
        };

        foreach (var prop in defType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null || !prop.CanWrite)
            {
                continue;
            }

            var jsonName = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            if (jsonName is "id")
            {
                continue; // already declared
            }

            properties[jsonName] = SchemaFor(prop.PropertyType);
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

    private static JsonObject SchemaFor(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
        {
            return new JsonObject { ["type"] = "string" };
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
            return new JsonObject(); // any scalar; refined by later milestones
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
                return new JsonObject { ["type"] = "array", ["items"] = SchemaFor(underlying.GetGenericArguments()[0]) };
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
        foreach (var prop in underlying.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.CanWrite)
            {
                nested[JsonNamingPolicy.CamelCase.ConvertName(prop.Name)] = SchemaFor(prop.PropertyType);
            }
        }

        return new JsonObject { ["type"] = "object", ["properties"] = nested };
    }
}
