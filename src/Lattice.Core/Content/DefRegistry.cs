using Lattice.Core.Formulas;

namespace Lattice.Core.Content;

/// <summary>
/// The central ID → definition hub (plan/01 §1). Instances hold def IDs and
/// resolve through this registry on use, which is what makes hot reload
/// visible immediately: replacing a def here changes what every instance
/// sees on its next lookup.
/// </summary>
public sealed class DefRegistry
{
    private readonly Dictionary<string, Def> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _idsBySourceFile = new(StringComparer.Ordinal);

    public int Count => _byId.Count;

    public IEnumerable<Def> AllDefs => _byId.Values;

    public bool Contains(string id) => _byId.ContainsKey(id);

    public bool TryGet<TDef>(string id, out TDef def)
        where TDef : Def
    {
        if (_byId.TryGetValue(id, out var found) && found is TDef typed)
        {
            def = typed;
            return true;
        }

        def = null!;
        return false;
    }

    public TDef Get<TDef>(string id)
        where TDef : Def
    {
        if (!_byId.TryGetValue(id, out var found))
        {
            throw new KeyNotFoundException($"No def with ID '{id}' is registered.");
        }

        return found as TDef
            ?? throw new InvalidCastException($"Def '{id}' is a {found.GetType().Name}, not a {typeof(TDef).Name}.");
    }

    public IEnumerable<TDef> All<TDef>()
        where TDef : Def
        => _byId.Values.OfType<TDef>();

    /// <summary>Add a new def; fails (with reason) on duplicate or empty ID.</summary>
    public bool TryAdd(Def def, out string? error)
    {
        if (string.IsNullOrWhiteSpace(def.Id))
        {
            error = $"Def of type {def.GetType().Name} (from {def.SourceFile ?? "?"}) has an empty 'id'.";
            return false;
        }

        if (_byId.TryGetValue(def.Id, out var existing))
        {
            error = $"Duplicate def ID '{def.Id}' in {def.SourceFile ?? "?"} (already defined in {existing.SourceFile ?? "?"}).";
            return false;
        }

        _byId[def.Id] = def;
        IndexSource(def);
        error = null;
        return true;
    }

    /// <summary>Add or overwrite — the hot-reload path.</summary>
    public void Replace(Def def)
    {
        if (_byId.TryGetValue(def.Id, out var old))
        {
            UnindexSource(old);
        }

        _byId[def.Id] = def;
        IndexSource(def);
    }

    /// <summary>Remove every def loaded from a given content file (file deleted, or pre-reload sweep). Returns removed IDs.</summary>
    public IReadOnlyList<string> RemoveBySourceFile(string sourceFile)
    {
        if (!_idsBySourceFile.TryGetValue(sourceFile, out var ids))
        {
            return [];
        }

        var removed = ids.ToList();
        foreach (var id in removed)
        {
            _byId.Remove(id);
        }

        _idsBySourceFile.Remove(sourceFile);
        return removed;
    }

    /// <summary>
    /// Link pass: report dangling cross-def references and unparseable
    /// formulas (plan/01 acceptance: validation groundwork).
    /// </summary>
    public void Validate(ContentLoadReport report, IFormulaEngine? formulas = null)
    {
        foreach (var def in _byId.Values)
        {
            foreach (var reference in def.GetReferences())
            {
                if (!_byId.ContainsKey(reference.TargetId))
                {
                    report.Errors.Add(
                        $"Dangling reference: '{reference.TargetId}' (at {reference.Context}, file {def.SourceFile ?? "?"}) does not exist.");
                }
            }

            if (formulas is null)
            {
                continue;
            }

            foreach (var formula in def.GetFormulas())
            {
                if (!formulas.TryParse(formula, out var error))
                {
                    report.Errors.Add($"Bad formula in def '{def.Id}': \"{formula}\" — {error}");
                }
            }
        }
    }

    private void IndexSource(Def def)
    {
        if (def.SourceFile is null)
        {
            return;
        }

        if (!_idsBySourceFile.TryGetValue(def.SourceFile, out var ids))
        {
            ids = new HashSet<string>(StringComparer.Ordinal);
            _idsBySourceFile[def.SourceFile] = ids;
        }

        ids.Add(def.Id);
    }

    private void UnindexSource(Def def)
    {
        if (def.SourceFile is not null && _idsBySourceFile.TryGetValue(def.SourceFile, out var ids))
        {
            ids.Remove(def.Id);
        }
    }
}
