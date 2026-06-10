using System.Text.Json;
using Lattice.Core.Hosting;

namespace Lattice.Core.Content;

/// <summary>Outcome of a content load: errors block (the batch is not applied); warnings log.</summary>
public sealed class ContentLoadReport
{
    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public int DefsLoaded { get; set; }

    public bool Ok => Errors.Count == 0;

    public void MergeFrom(ContentLoadReport other)
    {
        Errors.AddRange(other.Errors);
        Warnings.AddRange(other.Warnings);
        DefsLoaded += other.DefsLoaded;
    }
}

/// <summary>
/// Parses content JSON into defs. A file holds either a single def object or
/// an array of them; every def carries a <c>"type"</c> discriminator resolved
/// through the <see cref="DefTypeRegistry"/>. Parsing is staged: a file's
/// defs are only applied to the registry if the whole file parsed cleanly,
/// so a broken edit never half-applies (hot-reload resilience, plan/01 §6).
/// </summary>
public sealed class ContentLoader
{
    /// <summary>Serializer settings shared by content and saves: camelCase, comments + trailing commas tolerated.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private readonly DefTypeRegistry _types;

    public ContentLoader(DefTypeRegistry types)
    {
        _types = types;
    }

    /// <summary>Load every content file from a source into the registry.</summary>
    public ContentLoadReport LoadAll(IContentSource source, DefRegistry registry)
    {
        var report = new ContentLoadReport();
        foreach (var file in source.EnumerateFiles().OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            string text;
            try
            {
                text = source.ReadAllText(file);
            }
            catch (IOException ex)
            {
                report.Errors.Add($"{file.RelativePath}: cannot read — {ex.Message}");
                continue;
            }

            report.MergeFrom(LoadFile(file, text, registry, replace: false));
        }

        return report;
    }

    /// <summary>
    /// Parse one file and apply its defs. With <paramref name="replace"/>
    /// (hot reload) the file's previous defs are swept first and re-adds
    /// overwrite; without it, duplicates are errors.
    /// </summary>
    public ContentLoadReport LoadFile(ContentFile file, string text, DefRegistry registry, bool replace)
    {
        var report = new ContentLoadReport();
        var staged = new List<Def>();

        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            switch (doc.RootElement.ValueKind)
            {
                case JsonValueKind.Object:
                    ParseDef(doc.RootElement, file, report, staged);
                    break;
                case JsonValueKind.Array:
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        ParseDef(element, file, report, staged);
                    }

                    break;
                default:
                    report.Errors.Add($"{file.RelativePath}: root must be a def object or an array of defs.");
                    break;
            }
        }
        catch (JsonException ex)
        {
            report.Errors.Add($"{file.RelativePath}: invalid JSON — {ex.Message}");
        }

        if (!report.Ok)
        {
            return report; // staged defs discarded; registry untouched
        }

        if (replace)
        {
            registry.RemoveBySourceFile(file.RelativePath);
        }

        foreach (var def in staged)
        {
            if (replace)
            {
                registry.Replace(def);
            }
            else if (!registry.TryAdd(def, out var error))
            {
                report.Errors.Add(error!);
                continue;
            }

            report.DefsLoaded++;
        }

        return report;
    }

    private void ParseDef(JsonElement element, ContentFile file, ContentLoadReport report, List<Def> staged)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            report.Errors.Add($"{file.RelativePath}: expected a def object, found {element.ValueKind}.");
            return;
        }

        if (!element.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            report.Errors.Add($"{file.RelativePath}: def is missing the 'type' discriminator.");
            return;
        }

        var typeName = typeProp.GetString()!;
        if (!_types.TryGetClrType(typeName, out var clrType))
        {
            report.Errors.Add($"{file.RelativePath}: unknown def type '{typeName}'.");
            return;
        }

        Def def;
        try
        {
            def = (Def)element.Deserialize(clrType, JsonOptions)!;
        }
        catch (JsonException ex)
        {
            report.Errors.Add($"{file.RelativePath}: failed to deserialize '{typeName}' def — {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(def.Id))
        {
            report.Errors.Add($"{file.RelativePath}: '{typeName}' def has a missing or empty 'id'.");
            return;
        }

        def.SourceFile = file.RelativePath;
        staged.Add(def);
    }
}
