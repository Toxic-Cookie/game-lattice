using System.Text.Json;

namespace Lattice.Core.Content;

/// <summary>Conversions between <see cref="JsonElement"/> and the blackboard's plain value set (bool/double/string).</summary>
public static class JsonValueHelper
{
    /// <summary>Convert a JSON scalar to a plain CLR value. Returns false for objects/arrays/null.</summary>
    public static bool TryToPlain(JsonElement element, out object value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            case JsonValueKind.Number:
                value = element.GetDouble();
                return true;
            case JsonValueKind.String:
                value = element.GetString()!;
                return true;
            default:
                value = null!;
                return false;
        }
    }

    /// <summary>Parse a console/CLI literal into a plain value: true/false → bool, numeric → double, else string.</summary>
    public static object ParseLiteral(string text)
    {
        if (bool.TryParse(text, out var b))
        {
            return b;
        }

        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        return text;
    }
}
