using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lattice.Studio;

/// <summary>
/// Format-preserving editor for a single content JSON file. Locates a def by id
/// across the three container shapes (bare array, single object, or
/// <c>{"defs": [...]}</c>) and writes an edited def back by splicing only the
/// changed top-level value spans — so untouched defs and untouched fields stay
/// byte-identical and git diffs show exactly what changed. Falls back to a
/// whole-def re-render only when the property set itself changes (add/remove).
/// </summary>
public sealed class ContentDocument
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly byte[] _bytes; // file body, BOM stripped
    private readonly bool _hadBom;

    private ContentDocument(byte[] bytes, bool hadBom)
    {
        _bytes = bytes;
        _hadBom = hadBom;
    }

    public static ContentDocument Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        var hadBom = raw.Length >= 3 && raw[0] == Bom[0] && raw[1] == Bom[1] && raw[2] == Bom[2];
        var body = hadBom ? raw[3..] : raw;
        return new ContentDocument(body, hadBom);
    }

    /// <summary>The raw JSON object for a def, or null when this file does not contain it.</summary>
    public JsonObject? GetDef(string id)
    {
        if (!TryFindSpan(id, out var start, out var len))
        {
            return null;
        }

        return JsonNode.Parse(_bytes.AsSpan(start, len))!.AsObject();
    }

    public enum WriteOutcome
    {
        NotFound,
        Unchanged,
        Written,
    }

    /// <summary>Apply an edited def to the in-memory bytes; returns whether anything changed.</summary>
    public WriteOutcome ReplaceDef(string id, JsonObject newDef, out byte[] result)
    {
        result = _bytes;
        if (!TryFindSpan(id, out var start, out var len))
        {
            return WriteOutcome.NotFound;
        }

        var oldSpan = _bytes.AsSpan(start, len);
        var oldDef = JsonNode.Parse(oldSpan)!.AsObject();
        if (JsonNode.DeepEquals(oldDef, newDef))
        {
            return WriteOutcome.Unchanged;
        }

        var oldText = oldSpan.ToArray();
        byte[] newDefBytes = SamePropertySet(oldDef, newDef)
            ? SpliceChangedValues(oldText, oldDef, newDef)
            : Utf8.GetBytes(RenderDef(newDef, DetectPropertyIndent(oldText)));

        result = Concat(_bytes.AsSpan(0, start), newDefBytes, _bytes.AsSpan(start + len));
        return WriteOutcome.Written;
    }

    public void Save(string path, byte[] body)
    {
        using var stream = File.Create(path);
        if (_hadBom)
        {
            stream.Write(Bom);
        }

        stream.Write(body);
    }

    // --- locating the def object ------------------------------------------------

    private bool TryFindSpan(string id, out int start, out int len)
    {
        var reader = new Utf8JsonReader(_bytes);
        var stack = new Stack<(long Start, bool IsTarget)>();
        string? pendingName = null;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    stack.Push((reader.TokenStartIndex, false));
                    pendingName = null;
                    break;
                case JsonTokenType.EndObject:
                    var frame = stack.Pop();
                    if (frame.IsTarget)
                    {
                        start = (int)frame.Start;
                        len = (int)(reader.TokenStartIndex + 1 - frame.Start);
                        return true;
                    }

                    break;
                case JsonTokenType.PropertyName:
                    pendingName = reader.GetString();
                    break;
                case JsonTokenType.String:
                    if (pendingName == "id" && stack.Count > 0 && reader.GetString() == id)
                    {
                        var top = stack.Pop();
                        stack.Push((top.Start, true));
                    }

                    pendingName = null;
                    break;
                default:
                    pendingName = null;
                    break;
            }
        }

        start = 0;
        len = 0;
        return false;
    }

    // --- minimal-diff value splicing -------------------------------------------

    private static bool SamePropertySet(JsonObject a, JsonObject b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, _) in a)
        {
            if (!b.ContainsKey(key))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Rebuild the def text replacing only the value spans of changed top-level properties.</summary>
    private static byte[] SpliceChangedValues(byte[] oldText, JsonObject oldDef, JsonObject newDef)
    {
        var spans = TopLevelValueSpans(oldText);
        // Top-level property values sit one indent level in; render changed
        // object/array values there so multi-line replacements stay aligned.
        var propLevel = Math.Max(1, DetectPropertyIndent(oldText) / 2);
        // Apply right-to-left so earlier offsets stay valid.
        var edits = new List<(int Start, int Len, byte[] Replacement)>();
        foreach (var (key, value) in newDef)
        {
            if (!spans.TryGetValue(key, out var span))
            {
                continue;
            }

            if (!JsonNode.DeepEquals(oldDef[key], value))
            {
                edits.Add((span.Start, span.Len, Utf8.GetBytes(RenderValue(value, propLevel))));
            }
        }

        edits.Sort((x, y) => y.Start.CompareTo(x.Start));
        var buffer = oldText.ToList();
        foreach (var (start, len, replacement) in edits)
        {
            buffer.RemoveRange(start, len);
            buffer.InsertRange(start, replacement);
        }

        return [.. buffer];
    }

    /// <summary>Byte spans (into the def text) of each top-level property's value.</summary>
    private static Dictionary<string, (int Start, int Len)> TopLevelValueSpans(byte[] defText)
    {
        var spans = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        var reader = new Utf8JsonReader(defText);
        var depth = 0;
        string? name = null;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    name = depth == 1 ? reader.GetString() : null;
                    break;
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    if (depth == 1 && name is not null)
                    {
                        // Top-level container value: Skip() consumes through its matching
                        // End token, so depth is unchanged — do not increment.
                        var valueStart = (int)reader.TokenStartIndex;
                        reader.Skip();
                        spans[name] = (valueStart, (int)(reader.TokenStartIndex + 1) - valueStart);
                        name = null;
                    }
                    else
                    {
                        depth++;
                    }

                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    depth--;
                    break;
                case JsonTokenType.String:
                    if (depth == 1 && name is not null)
                    {
                        var valueStart = (int)reader.TokenStartIndex;
                        spans[name] = (valueStart, reader.ValueSpan.Length + 2); // + surrounding quotes
                        name = null;
                    }

                    break;
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    if (depth == 1 && name is not null)
                    {
                        spans[name] = ((int)reader.TokenStartIndex, reader.ValueSpan.Length);
                        name = null;
                    }

                    break;
            }
        }

        return spans;
    }

    // --- rendering (style-matched to the content conventions) -------------------

    /// <summary>Render a value the way the content files do: primitives and all-primitive arrays inline.</summary>
    private static string RenderValue(JsonNode? value, int indent)
    {
        switch (value)
        {
            case null:
                return "null";
            case JsonArray array:
                if (array.Count == 0)
                {
                    return "[]";
                }

                if (array.All(IsPrimitive))
                {
                    return "[" + string.Join(", ", array.Select(e => e!.ToJsonString())) + "]";
                }

                var pad = new string(' ', (indent + 1) * 2);
                var closePad = new string(' ', indent * 2);
                return "[\n" + string.Join(",\n", array.Select(e => pad + RenderValue(e, indent + 1))) + "\n" + closePad + "]";
            case JsonObject obj:
                if (obj.Count == 0)
                {
                    return "{}";
                }

                // Convention: objects whose values are all primitives render on one
                // line (tasks, effects, modifiers); objects with nested structure
                // expand (entity stats). Matches the hand-authored content style.
                if (obj.All(kv => IsPrimitive(kv.Value)))
                {
                    return "{ " + string.Join(", ", obj.Select(kv => $"{JsonSerializer.Serialize(kv.Key)}: {RenderValue(kv.Value, indent)}")) + " }";
                }

                var ipad = new string(' ', (indent + 1) * 2);
                var ocpad = new string(' ', indent * 2);
                return "{\n"
                    + string.Join(",\n", obj.Select(kv => $"{ipad}{JsonSerializer.Serialize(kv.Key)}: {RenderValue(kv.Value, indent + 1)}"))
                    + "\n" + ocpad + "}";
            default:
                return value.ToJsonString();
        }
    }

    /// <summary>Whole-def render (structural-change fallback), property block indented to match the file.</summary>
    private static string RenderDef(JsonObject def, int propertyIndent)
    {
        var propLevel = Math.Max(1, propertyIndent / 2);
        var pad = new string(' ', propLevel * 2);
        var closePad = new string(' ', (propLevel - 1) * 2);
        return "{\n"
            + string.Join(",\n", def.Select(kv => $"{pad}{JsonSerializer.Serialize(kv.Key)}: {RenderValue(kv.Value, propLevel)}"))
            + "\n" + closePad + "}";
    }

    private static int DetectPropertyIndent(byte[] defText)
    {
        var text = Utf8.GetString(defText);
        var nl = text.IndexOf('\n');
        if (nl < 0)
        {
            return 2;
        }

        var i = nl + 1;
        var count = 0;
        while (i < text.Length && text[i] == ' ')
        {
            count++;
            i++;
        }

        return count == 0 ? 2 : count;
    }

    private static bool IsPrimitive(JsonNode? node) => node is null or JsonValue;

    private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ReadOnlySpan<byte> c)
    {
        var result = new byte[a.Length + b.Length + c.Length];
        a.CopyTo(result);
        b.CopyTo(result.AsSpan(a.Length));
        c.CopyTo(result.AsSpan(a.Length + b.Length));
        return result;
    }
}
