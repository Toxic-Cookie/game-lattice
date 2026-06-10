namespace Lattice.Core.Content;

/// <summary>
/// Maps the JSON <c>"type"</c> discriminator string to the CLR def type.
/// Built-in kinds are registered by <see cref="CreateDefault"/>; each later
/// milestone (and eventually mods/host code) registers its own kinds here.
/// The schema generator (plan/06 §2) enumerates this registry.
/// </summary>
public sealed class DefTypeRegistry
{
    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);

    public IEnumerable<KeyValuePair<string, Type>> All => _byName;

    /// <param name="typeName">The JSON discriminator value.</param>
    /// <param name="replace">Allow overriding an existing registration — modules may substitute
    /// an extended def subclass for a built-in kind (e.g. the RPG entity template).</param>
    public void Register<TDef>(string typeName, bool replace = false)
        where TDef : Def
    {
        if (!replace && _byName.ContainsKey(typeName))
        {
            throw new InvalidOperationException($"Def type '{typeName}' is already registered.");
        }

        _byName[typeName] = typeof(TDef);
    }

    public bool TryGetClrType(string typeName, out Type clrType) => _byName.TryGetValue(typeName, out clrType!);

    /// <summary>Registry with all built-in def kinds (M1: lifecycle, entity).</summary>
    public static DefTypeRegistry CreateDefault()
    {
        var registry = new DefTypeRegistry();
        registry.Register<LifecycleDef>("lifecycle");
        registry.Register<EntityTemplateDef>("entity");
        return registry;
    }
}
