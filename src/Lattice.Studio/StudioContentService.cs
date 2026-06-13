using System.Text.Json;
using System.Text.Json.Nodes;
using Lattice.Core.Content;
using Lattice.Core.Hosting.Standalone;
using Lattice.Tooling;

namespace Lattice.Studio;

/// <summary>
/// The editor's in-process bridge to the real content pipeline. Holds the
/// shared <see cref="ToolingContext"/> and serves the three read-only views the
/// browser needs — generated schemas, the def/primitive catalog, and a flat def
/// index — plus authoritative validation. Every answer derives from the same
/// loader, schema generator, and validators the CLI/CI use, so the editor can
/// never present a different picture of the content than the runtime sees.
/// </summary>
public sealed class StudioContentService
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<Type, string> _kindByType;

    public StudioContentService(string contentDir)
    {
        ContentDir = Path.GetFullPath(contentDir);
        Context = ToolingContext.Create();

        // The schema generator reads the union vocabularies off a static; seed it
        // once from the same registries the manifest/validate paths use.
        SchemaGenerator.UnionVocabularies = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["effect"] = Context.Effects.All.Select(e => e.Type).ToArray(),
            ["condition"] = Context.Conditions.All.Select(c => c.Type).ToArray(),
            ["task"] = Context.Tasks.All.Select(t => t.Type).ToArray(),
        };

        _kindByType = Context.Types.All.ToDictionary(kv => kv.Value, kv => kv.Key);
    }

    public string ContentDir { get; }

    public ToolingContext Context { get; }

    /// <summary>Per-kind JSON schemas plus the combined file schema (the form-rendering contract).</summary>
    public JsonObject Schemas()
    {
        var kinds = new JsonObject();
        foreach (var (typeName, clrType) in Context.Types.All.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            kinds[typeName] = JsonNode.Parse(SchemaGenerator.GenerateSchemaJson(typeName, clrType));
        }

        return new JsonObject
        {
            ["kinds"] = kinds,
            ["combined"] = JsonNode.Parse(
                SchemaGenerator.GenerateCombinedSchemaJson(Context.Types.All.Select(k => k.Key))),
        };
    }

    /// <summary>The manifest catalog (every def + every primitive's arg signature) as structured JSON.</summary>
    public JsonNode Catalog()
    {
        var registry = Load();
        var json = ManifestGenerator.GenerateJson(
            registry, Context.Types, Context.Effects, Context.Conditions, Context.Tasks);
        return JsonNode.Parse(json)!;
    }

    /// <summary>A flat index of every loaded def: id, kind, description, blueprint parent, and origin file.</summary>
    public ContentIndex Index()
    {
        var registry = Load();
        var entries = registry.AllDefs
            .Select(d => new DefIndexEntry(
                d.Id,
                _kindByType.TryGetValue(d.GetType(), out var kind) ? kind : d.GetType().Name,
                d.Description,
                d.Inherits,
                d.SourceFile))
            .OrderBy(e => e.Kind, StringComparer.Ordinal)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

        var files = entries
            .Select(e => e.SourceFile)
            .Where(f => f is not null)
            .Distinct()
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        return new ContentIndex(entries.Count, entries, files!);
    }

    /// <summary>Run the full validation pipeline — identical to <c>lattice validate</c>.</summary>
    public ValidationResult Validate() => ContentValidation.Run(Context, ContentDir);

    /// <summary>The raw JSON object for one def, plus its kind and origin file (for the form editor).</summary>
    public DefPayload? GetDef(string id)
    {
        var (def, path) = FindByFile(id);
        if (def is null)
        {
            return null;
        }

        var obj = ContentDocument.Load(path).GetDef(id);
        return obj is null ? null : new DefPayload(KindOf(def), def.SourceFile!, obj);
    }

    /// <summary>Apply an edited def to its source file (minimal-diff), then re-validate the whole tree.</summary>
    public SaveResult SaveDef(string id, JsonObject newDef)
    {
        var (def, path) = FindByFile(id);
        if (def is null)
        {
            return new SaveResult("not_found", false, null);
        }

        if (newDef["id"]?.GetValue<string>() != id)
        {
            return new SaveResult("error", false, null, "The def 'id' cannot be changed.");
        }

        if (newDef["type"]?.GetValue<string>() != KindOf(def))
        {
            return new SaveResult("error", false, null, "The def 'type' cannot be changed.");
        }

        var doc = ContentDocument.Load(path);
        var outcome = doc.ReplaceDef(id, newDef, out var bytes);
        if (outcome == ContentDocument.WriteOutcome.NotFound)
        {
            return new SaveResult("not_found", false, null);
        }

        var written = outcome == ContentDocument.WriteOutcome.Written;
        if (written)
        {
            doc.Save(path, bytes);
        }

        return new SaveResult(written ? "written" : "unchanged", written, Validate());
    }

    /// <summary>Create a new def: route it to a file (or use the requested one), append, then re-validate.</summary>
    public SaveResult CreateDef(JsonObject def, string? file)
    {
        if (StringProp(def, "id") is not { Length: > 0 } id)
        {
            return new SaveResult("error", false, null, "The def needs a non-empty string 'id'.");
        }

        if (StringProp(def, "type") is not { Length: > 0 } type || !Context.Types.TryGetClrType(type, out _))
        {
            return new SaveResult("error", false, null, "The def needs a known 'type'.");
        }

        var registry = Load();
        if (registry.Contains(id))
        {
            return new SaveResult("error", false, null, $"A def with id '{id}' already exists.");
        }

        var rel = string.IsNullOrWhiteSpace(file) ? RouteFile(type, registry) : file!;
        var path = Path.Combine(ContentDir, rel);
        if (File.Exists(path))
        {
            var doc = ContentDocument.Load(path);
            doc.Save(path, doc.AppendDef(def));
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, ContentDocument.NewFile(def));
        }

        return new SaveResult("written", true, Validate());
    }

    public string Serialize(object value) => JsonSerializer.Serialize(value, Web);

    private static string? StringProp(JsonObject obj, string name)
        => obj.TryGetPropertyValue(name, out var node) && node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private string KindOf(Def def) => _kindByType.TryGetValue(def.GetType(), out var k) ? k : def.GetType().Name;

    /// <summary>The file most existing defs of this kind already live in (majority wins); a sensible default otherwise.</summary>
    private string RouteFile(string type, DefRegistry registry)
    {
        Context.Types.TryGetClrType(type, out var clr);
        var byFile = registry.AllDefs
            .Where(d => d.GetType() == clr && d.SourceFile is not null)
            .GroupBy(d => d.SourceFile!)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        return byFile?.Key ?? $"{type}.json";
    }

    private (Def? Def, string Path) FindByFile(string id)
    {
        var def = Load().AllDefs.FirstOrDefault(d => d.Id == id);
        return def?.SourceFile is null ? (null, "") : (def, Path.Combine(ContentDir, def.SourceFile));
    }

    private DefRegistry Load()
    {
        using var source = new DirectoryContentSource(ContentDir, watch: false);
        var registry = new DefRegistry();
        new ContentLoader(Context.Types).LoadAll(source, registry);
        return registry;
    }
}

/// <summary>A def's raw JSON plus the metadata the form editor needs.</summary>
public sealed record DefPayload(string Kind, string SourceFile, JsonObject Def);

/// <summary>Outcome of a save: status is "written" | "unchanged" | "not_found" | "error".</summary>
public sealed record SaveResult(string Status, bool Written, ValidationResult? Validation, string? Error = null);

/// <summary>One row in the master browser.</summary>
public sealed record DefIndexEntry(string Id, string Kind, string? Description, string? Inherits, string? SourceFile);

/// <summary>The flat def index plus the distinct set of source files (for the file facet).</summary>
public sealed record ContentIndex(int Count, IReadOnlyList<DefIndexEntry> Defs, IReadOnlyList<string> Files);
