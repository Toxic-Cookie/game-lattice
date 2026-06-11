using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lattice.Core.Content;

/// <summary>
/// Prefab blueprinting (plan/06 §4): any def may declare
/// <c>"inherits": "some_parent_id"</c>; the parent chain is resolved at the
/// raw-JSON level *before* deserialization. Merge semantics are deliberately
/// explicit (LLMs handle operators better than merge magic):
/// objects deep-merge, scalars override, arrays replace — unless the child
/// supplies <c>{"$append": [...]}</c> and/or <c>{"$remove": [...]}</c> for an
/// array property.
/// </summary>
public sealed class BlueprintResolver
{
    public const int MaxDepth = 8;

    private readonly Dictionary<string, RawDef> _rawById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _idsByFile = new(StringComparer.Ordinal);

    public readonly struct RawDef(string id, string typeName, JsonElement element, string sourceFile)
    {
        public string Id { get; } = id;

        public string TypeName { get; } = typeName;

        /// <summary>Detached (cloned) element — safe past document disposal.</summary>
        public JsonElement Element { get; } = element;

        public string SourceFile { get; } = sourceFile;
    }

    /// <summary>All known raw defs (manifest tooling walks these for hierarchy listings).</summary>
    public IReadOnlyDictionary<string, RawDef> Raw => _rawById;

    public void Remember(RawDef raw)
    {
        _rawById[raw.Id] = raw;
        if (!_idsByFile.TryGetValue(raw.SourceFile, out var ids))
        {
            ids = [];
            _idsByFile[raw.SourceFile] = ids;
        }

        ids.Add(raw.Id);
    }

    /// <summary>Drop everything previously remembered from a file (pre-reload sweep).</summary>
    public void ForgetFile(string sourceFile)
    {
        if (_idsByFile.TryGetValue(sourceFile, out var ids))
        {
            foreach (var id in ids)
            {
                // only forget if the entry still belongs to this file (a reload may have re-claimed the id)
                if (_rawById.TryGetValue(id, out var raw) && raw.SourceFile == sourceFile)
                {
                    _rawById.Remove(id);
                }
            }

            _idsByFile.Remove(sourceFile);
        }
    }

    public void Clear()
    {
        _rawById.Clear();
        _idsByFile.Clear();
    }

    /// <summary>
    /// Resolve a def's inheritance chain into a final merged JSON object,
    /// or null with an error message. Defs without "inherits" pass through.
    /// </summary>
    public JsonObject? Resolve(RawDef raw, out string? error)
        => ResolveCore(raw, new HashSet<string>(StringComparer.Ordinal), depth: 0, out error);

    private JsonObject? ResolveCore(RawDef raw, HashSet<string> visiting, int depth, out string? error)
    {
        error = null;
        if (depth > MaxDepth)
        {
            error = $"def '{raw.Id}': inheritance deeper than {MaxDepth} levels.";
            return null;
        }

        var node = JsonNode.Parse(raw.Element.GetRawText())!.AsObject();
        if (!TryGetInherits(node, out var parentId))
        {
            return node;
        }

        if (!visiting.Add(raw.Id))
        {
            error = $"def '{raw.Id}': inheritance cycle ({string.Join(" -> ", visiting)} -> {raw.Id}).";
            return null;
        }

        if (!_rawById.TryGetValue(parentId, out var parent))
        {
            error = $"def '{raw.Id}': inherits unknown def '{parentId}'.";
            return null;
        }

        if (!string.Equals(parent.TypeName, raw.TypeName, StringComparison.Ordinal))
        {
            error = $"def '{raw.Id}' ({raw.TypeName}) cannot inherit '{parentId}' ({parent.TypeName}) — kinds must match.";
            return null;
        }

        var resolvedParent = ResolveCore(parent, visiting, depth + 1, out error);
        if (resolvedParent is null)
        {
            return null;
        }

        visiting.Remove(raw.Id);
        return Merge(resolvedParent, node, raw.Id, ref error);
    }

    private static bool TryGetInherits(JsonObject node, out string parentId)
    {
        parentId = "";
        if (node.TryGetPropertyValue("inherits", out var value) && value is JsonValue v
            && v.TryGetValue<string>(out var s) && s.Length > 0)
        {
            parentId = s;
            return true;
        }

        return false;
    }

    /// <summary>Child over parent: objects deep-merge, scalars/arrays override, $append/$remove edit parent arrays.</summary>
    private static JsonObject? Merge(JsonObject parent, JsonObject child, string defId, ref string? error)
    {
        var result = parent.DeepClone().AsObject();
        result.Remove("inherits"); // the parent's link is consumed; the child's (if any) is re-set below

        foreach (var pair in child)
        {
            var childValue = pair.Value;
            result.TryGetPropertyValue(pair.Key, out var parentValue);

            if (childValue is JsonObject childObject && IsArrayPatch(childObject))
            {
                var patched = ApplyArrayPatch(parentValue as JsonArray, childObject, defId, pair.Key, ref error);
                if (error is not null)
                {
                    return null;
                }

                result[pair.Key] = patched;
            }
            else if (childValue is JsonObject nestedChild && parentValue is JsonObject nestedParent)
            {
                var merged = Merge(nestedParent, nestedChild, defId, ref error);
                if (error is not null)
                {
                    return null;
                }

                result[pair.Key] = merged;
            }
            else
            {
                result[pair.Key] = childValue?.DeepClone();
            }
        }

        return result;
    }

    private static bool IsArrayPatch(JsonObject node)
        => node.ContainsKey("$append") || node.ContainsKey("$remove");

    private static JsonArray? ApplyArrayPatch(JsonArray? parentArray, JsonObject patch, string defId, string property, ref string? error)
    {
        foreach (var key in patch.Select(p => p.Key))
        {
            if (key is not ("$append" or "$remove"))
            {
                error = $"def '{defId}': array patch for '{property}' may only contain $append/$remove (found '{key}').";
                return null;
            }
        }

        var result = parentArray?.DeepClone().AsArray() ?? [];

        if (patch.TryGetPropertyValue("$remove", out var remove))
        {
            if (remove is not JsonArray removals)
            {
                error = $"def '{defId}': $remove for '{property}' must be an array.";
                return null;
            }

            foreach (var unwanted in removals)
            {
                for (var i = result.Count - 1; i >= 0; i--)
                {
                    if (JsonNode.DeepEquals(result[i], unwanted))
                    {
                        result.RemoveAt(i);
                    }
                }
            }
        }

        if (patch.TryGetPropertyValue("$append", out var append))
        {
            if (append is not JsonArray additions)
            {
                error = $"def '{defId}': $append for '{property}' must be an array.";
                return null;
            }

            foreach (var item in additions)
            {
                result.Add(item?.DeepClone());
            }
        }

        return result;
    }
}
