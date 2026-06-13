using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Lattice.Tooling;

/// <summary>
/// Reads <c>&lt;summary&gt;</c> text from the XML documentation files emitted
/// next to the def assemblies, so the schema generator (and the editor) can
/// surface each def kind's own doc comment as its description — no second
/// source of truth, and it can never drift from the type it documents.
/// </summary>
public static class XmlDocs
{
    private static readonly ConcurrentDictionary<string, XDocument?> Cache = new(StringComparer.Ordinal);

    /// <summary>The cleaned, single-paragraph summary for a type, or null when undocumented.</summary>
    public static string? Summary(Type type) => Lookup(type.Assembly, "T:" + type.FullName);

    /// <summary>The cleaned summary for a property (read from its declaring type's assembly), or null.</summary>
    public static string? Summary(PropertyInfo property)
        => property.DeclaringType is { } declaring
            ? Lookup(declaring.Assembly, $"P:{declaring.FullName}.{property.Name}")
            : null;

    private static string? Lookup(Assembly assembly, string memberName)
    {
        if (string.IsNullOrEmpty(assembly.Location))
        {
            return null;
        }

        var doc = Cache.GetOrAdd(Path.ChangeExtension(assembly.Location, ".xml"), p => File.Exists(p) ? XDocument.Load(p) : null);
        var summary = doc?.Root?.Element("members")?.Elements("member")
            .FirstOrDefault(m => (string?)m.Attribute("name") == memberName)
            ?.Element("summary");

        return summary is null ? null : Clean(summary);
    }

    private static string Clean(XElement summary)
    {
        var sb = new StringBuilder();
        Flatten(summary, sb);
        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void Flatten(XNode node, StringBuilder sb)
    {
        switch (node)
        {
            case XText text:
                sb.Append(text.Value);
                break;
            case XElement el when el.Name == "see" || el.Name == "seealso"
                                  || el.Name == "paramref" || el.Name == "typeparamref":
                // Render a cross-reference as its short name (inner text wins if present).
                if (!string.IsNullOrWhiteSpace(el.Value))
                {
                    sb.Append(el.Value);
                }
                else
                {
                    var reference = (string?)el.Attribute("cref")
                        ?? (string?)el.Attribute("langword")
                        ?? (string?)el.Attribute("name");
                    if (reference is not null)
                    {
                        var cut = Math.Max(reference.LastIndexOf('.'), reference.IndexOf(':'));
                        sb.Append(reference[(cut + 1)..]);
                    }
                }

                break;
            case XElement el:
                foreach (var child in el.Nodes())
                {
                    Flatten(child, sb);
                }

                break;
        }
    }
}
