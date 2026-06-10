using Lattice.Ai.Perception;

namespace Lattice.Ai.Tests;

public class ConditionSetTests
{
    [Fact]
    public void Catalog_AssignsBitsInDeclarationOrder()
    {
        var catalog = new ConditionCatalog(["A", "B", "C"]);

        Assert.True(catalog.TryGetBit("A", out var a));
        Assert.True(catalog.TryGetBit("C", out var c));
        Assert.Equal(0, a);
        Assert.Equal(2, c);
        Assert.False(catalog.TryGetBit("MISSING", out _));
    }

    [Fact]
    public void Catalog_RejectsMoreThan32()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ConditionCatalog(Enumerable.Range(0, 33).Select(i => $"C{i}")));
    }

    [Fact]
    public void MaskOperations_Work()
    {
        var catalog = new ConditionCatalog(["SEE", "HEAR", "HURT"]);
        var set = new ConditionSet();
        set.Set(catalog, "SEE");
        set.Set(catalog, "HURT");

        Assert.True(set.IsSet(catalog, "SEE"));
        Assert.False(set.IsSet(catalog, "HEAR"));
        Assert.True(set.HasAllOf(catalog.MaskOf(["SEE", "HURT"])));
        Assert.False(set.HasAllOf(catalog.MaskOf(["SEE", "HEAR"])));
        Assert.True(set.HasAnyOf(catalog.MaskOf(["HEAR", "HURT"])));
        Assert.Equal(["SEE", "HURT"], set.SetNames(catalog));

        set.ClearAll();
        Assert.False(set.HasAnyOf(uint.MaxValue));
    }

    [Fact]
    public void UnknownNamesInMask_AreSkipped()
    {
        var catalog = new ConditionCatalog(["A"]);

        Assert.Equal(0u, catalog.MaskOf(["NOPE"]));
        Assert.Equal(1u, catalog.MaskOf(["A", "NOPE"]));
    }
}
