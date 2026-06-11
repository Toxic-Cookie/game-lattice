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

    /// <summary>Blueprint inheritance state (raw JSON by ID); kept so hot reloads can resolve cross-file parents.</summary>
    public BlueprintResolver Blueprints { get; } = new();

    /// <summary>A content pack's manifest (plan/06 §5): pack.json inside a pack directory.</summary>
    public sealed class PackManifest
    {
        public string Id { get; set; } = "";

        public string? Version { get; set; }

        /// <summary>Load order among packs (lower loads first; later packs override same-ID defs).</summary>
        public double Priority { get; set; }

        /// <summary>Pack IDs that must load before this one.</summary>
        public List<string>? Dependencies { get; set; }
    }

    /// <summary>
    /// Load every content file from a source into the registry. Directories
    /// containing a <c>pack.json</c> are content packs (plan/06 §5): the base
    /// content loads first, then packs ordered by priority/dependencies, and
    /// a later pack may *override* a same-ID def from an earlier scope —
    /// the registry-overlay mechanism mods share with seasons.
    /// </summary>
    public ContentLoadReport LoadAll(IContentSource source, DefRegistry registry)
    {
        var report = new ContentLoadReport();
        Blueprints.Clear();

        // read everything up front; partition files into base vs pack scopes
        var texts = new List<(ContentFile File, string Text)>();
        foreach (var file in source.EnumerateFiles().OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            try
            {
                texts.Add((file, source.ReadAllText(file)));
            }
            catch (IOException ex)
            {
                report.Errors.Add($"{file.RelativePath}: cannot read — {ex.Message}");
            }
        }

        var packs = ParsePackManifests(texts, report);
        var scopes = OrderScopes(packs, report);
        var scopeOf = (ContentFile file) =>
            packs.Keys.FirstOrDefault(prefix => file.RelativePath.StartsWith(prefix, StringComparison.Ordinal)) ?? "";

        // pass 1: gather raw defs scope by scope so later packs own contested IDs
        var parsed = new List<(int Scope, ContentFile File, List<BlueprintResolver.RawDef> Raws)>();
        foreach (var (scopeIndex, prefix) in scopes.Select((p, i) => (i, p)))
        {
            foreach (var (file, text) in texts)
            {
                if (file.RelativePath.EndsWith("pack.json", StringComparison.Ordinal) || scopeOf(file) != prefix)
                {
                    continue;
                }

                var fileReport = new ContentLoadReport();
                var raws = ParseRawDefs(file, text, fileReport);
                report.MergeFrom(fileReport);
                if (fileReport.Ok)
                {
                    foreach (var raw in raws)
                    {
                        Blueprints.Remember(raw);
                    }

                    parsed.Add((scopeIndex, file, raws));
                }
            }
        }

        // pass 2: apply in scope order; later scopes override earlier same-IDs
        var scopeOfId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (scope, file, raws) in parsed)
        {
            report.MergeFrom(ApplyRawDefs(file, raws, registry, replace: false, scope, scopeOfId));
        }

        return report;
    }

    private static Dictionary<string, PackManifest> ParsePackManifests(
        List<(ContentFile File, string Text)> texts, ContentLoadReport report)
    {
        var packs = new Dictionary<string, PackManifest>(StringComparer.Ordinal);
        foreach (var (file, text) in texts)
        {
            if (!file.RelativePath.EndsWith("pack.json", StringComparison.Ordinal))
            {
                continue;
            }

            var prefixLength = file.RelativePath.Length - "pack.json".Length;
            var prefix = file.RelativePath[..prefixLength];
            if (prefix.Length == 0)
            {
                report.Errors.Add($"{file.RelativePath}: pack.json must live inside a pack directory, not the content root.");
                continue;
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<PackManifest>(text, JsonOptions);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    report.Errors.Add($"{file.RelativePath}: pack.json needs an 'id'.");
                    continue;
                }

                packs[prefix] = manifest;
            }
            catch (JsonException ex)
            {
                report.Errors.Add($"{file.RelativePath}: invalid pack.json — {ex.Message}");
            }
        }

        return packs;
    }

    /// <summary>Scope load order: "" (base) first, then packs by dependencies, priority, id. Returns scope prefixes.</summary>
    private static List<string> OrderScopes(Dictionary<string, PackManifest> packs, ContentLoadReport report)
    {
        var scopes = new List<string> { "" };
        var remaining = packs
            .OrderBy(p => p.Value.Priority)
            .ThenBy(p => p.Value.Id, StringComparer.Ordinal)
            .ToList();
        var loadedIds = new HashSet<string>(StringComparer.Ordinal);
        var knownIds = new HashSet<string>(packs.Values.Select(p => p.Id), StringComparer.Ordinal);

        while (remaining.Count > 0)
        {
            var index = remaining.FindIndex(p => (p.Value.Dependencies ?? []).All(loadedIds.Contains));
            if (index < 0)
            {
                foreach (var (prefix, manifest) in remaining)
                {
                    var missing = (manifest.Dependencies ?? []).Where(d => !knownIds.Contains(d)).ToList();
                    report.Errors.Add(missing.Count > 0
                        ? $"Pack '{manifest.Id}' ({prefix}) depends on missing pack(s): {string.Join(", ", missing)}."
                        : $"Pack '{manifest.Id}' ({prefix}) is part of a dependency cycle.");
                }

                break;
            }

            scopes.Add(remaining[index].Key);
            loadedIds.Add(remaining[index].Value.Id);
            remaining.RemoveAt(index);
        }

        return scopes;
    }

    /// <summary>
    /// Parse one file and apply its defs. With <paramref name="replace"/>
    /// (hot reload) the file's previous defs are swept first and re-adds
    /// overwrite; without it, duplicates are errors. Blueprint parents in
    /// *other* files resolve against the last full load's raw cache; reloading
    /// a parent does not re-resolve already-loaded children until their own
    /// files reload.
    /// </summary>
    public ContentLoadReport LoadFile(ContentFile file, string text, DefRegistry registry, bool replace)
    {
        var report = new ContentLoadReport();
        var raws = ParseRawDefs(file, text, report);
        if (!report.Ok)
        {
            return report; // registry and raw cache untouched
        }

        if (replace)
        {
            Blueprints.ForgetFile(file.RelativePath);
        }

        foreach (var raw in raws)
        {
            Blueprints.Remember(raw);
        }

        report.MergeFrom(ApplyRawDefs(file, raws, registry, replace));
        return report;
    }

    private List<BlueprintResolver.RawDef> ParseRawDefs(ContentFile file, string text, ContentLoadReport report)
    {
        var raws = new List<BlueprintResolver.RawDef>();
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            switch (doc.RootElement.ValueKind)
            {
                // wrapper form: { "$schema": "...", "defs": [...] } — lets
                // multi-def files carry an IDE/LLM schema header (plan/06 §2)
                case JsonValueKind.Object when !doc.RootElement.TryGetProperty("type", out _)
                                               && doc.RootElement.TryGetProperty("defs", out var defs)
                                               && defs.ValueKind == JsonValueKind.Array:
                    foreach (var element in defs.EnumerateArray())
                    {
                        ParseRaw(element, file, report, raws);
                    }

                    break;
                case JsonValueKind.Object:
                    ParseRaw(doc.RootElement, file, report, raws);
                    break;
                case JsonValueKind.Array:
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        ParseRaw(element, file, report, raws);
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

        return raws;
    }

    private void ParseRaw(JsonElement element, ContentFile file, ContentLoadReport report, List<BlueprintResolver.RawDef> raws)
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
        if (!_types.TryGetClrType(typeName, out _))
        {
            report.Errors.Add($"{file.RelativePath}: unknown def type '{typeName}'.");
            return;
        }

        if (!element.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idProp.GetString()))
        {
            report.Errors.Add($"{file.RelativePath}: '{typeName}' def has a missing or empty 'id'.");
            return;
        }

        raws.Add(new BlueprintResolver.RawDef(idProp.GetString()!, typeName, element.Clone(), file.RelativePath));
    }

    private ContentLoadReport ApplyRawDefs(
        ContentFile file, List<BlueprintResolver.RawDef> raws, DefRegistry registry, bool replace,
        int scope = 0, Dictionary<string, int>? scopeOfId = null)
    {
        var report = new ContentLoadReport();
        var staged = new List<Def>();

        foreach (var raw in raws)
        {
            var merged = Blueprints.Resolve(raw, out var blueprintError);
            if (merged is null)
            {
                report.Errors.Add($"{file.RelativePath}: {blueprintError}");
                continue;
            }

            _types.TryGetClrType(raw.TypeName, out var clrType);
            Def def;
            try
            {
                def = (Def)merged.Deserialize(clrType!, JsonOptions)!;
            }
            catch (JsonException ex)
            {
                report.Errors.Add($"{file.RelativePath}: failed to deserialize '{raw.TypeName}' def — {ex.Message}");
                continue;
            }

            def.SourceFile = file.RelativePath;
            staged.Add(def);
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
            var overridesEarlierScope = scopeOfId is not null
                                        && scopeOfId.TryGetValue(def.Id, out var ownerScope)
                                        && ownerScope < scope;
            if (replace || overridesEarlierScope)
            {
                registry.Replace(def); // hot reload, or a later pack overlaying an earlier def
            }
            else if (!registry.TryAdd(def, out var error))
            {
                report.Errors.Add(error!);
                continue;
            }

            scopeOfId?.TryAdd(def.Id, scope);
            if (scopeOfId is not null && overridesEarlierScope)
            {
                scopeOfId[def.Id] = scope;
            }

            report.DefsLoaded++;
        }

        return report;
    }
}
