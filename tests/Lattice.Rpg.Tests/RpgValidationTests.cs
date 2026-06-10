using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;

namespace Lattice.Rpg.Tests;

public sealed class RpgValidationTests : IDisposable
{
    private readonly RpgTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private ContentLoadReport Validate()
    {
        using var source = new DirectoryContentSource(_host.ContentRoot, watch: false);
        var registry = new DefRegistry();
        var loader = new ContentLoader(LatticeRpg.CreateDefTypes());
        var report = loader.LoadAll(source, registry);
        var formulas = new NCalcFormulaEngine(new LatticeRandom(0));
        registry.Validate(report, formulas);
        new RpgContentValidator().Validate(registry, report, formulas);
        return report;
    }

    [Fact]
    public void StandardContent_Validates()
    {
        _host.WriteStandardContent();
        var report = Validate();

        Assert.True(report.Ok, string.Join("; ", report.Errors));
    }

    [Fact]
    public void UnknownStatKeyInFormula_IsError()
    {
        _host.WriteContent("stats.json", """
            [ { "id": "stat_hp", "type": "stat", "key": "HP", "max": "Vigor * 10" } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("unknown stat key 'Vigor'"));
    }

    [Fact]
    public void StatDependencyCycle_IsError()
    {
        _host.WriteContent("stats.json", """
            [ { "id": "stat_a", "type": "stat", "key": "A", "formula": "B + 1" },
              { "id": "stat_b", "type": "stat", "key": "B", "formula": "A + 1" } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("Stat dependency cycle"));
    }

    [Fact]
    public void DuplicateStatKey_IsError()
    {
        _host.WriteContent("stats.json", """
            [ { "id": "stat_a", "type": "stat", "key": "HP" },
              { "id": "stat_b", "type": "stat", "key": "HP" } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("declared by more than one stat"));
    }

    [Fact]
    public void LootTableCycle_IsError()
    {
        _host.WriteContent("loot.json", """
            [ { "id": "loot_a", "type": "loot", "entries": [ { "tableRef": "loot_b", "weight": 1 } ] },
              { "id": "loot_b", "type": "loot", "entries": [ { "tableRef": "loot_a", "weight": 1 } ] } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("Loot table cycle"));
    }

    [Fact]
    public void BadSlotReference_IsError()
    {
        _host.WriteContent("bad-slot.json", """
            [ { "id": "stat_x", "type": "stat", "key": "X" },
              { "id": "item_a", "type": "item", "slot": "stat_x" } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("is not a slot def"));
    }

    [Fact]
    public void DanglingItemReference_IsError()
    {
        _host.WriteContent("loot.json", """
            { "id": "loot_x", "type": "loot", "entries": [ { "item": "item_missing", "weight": 1 } ] }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("item_missing"));
    }

    [Fact]
    public void PeriodicEffectInEquipEffects_IsError()
    {
        _host.WriteContent("bad-item.json", """
            [ { "id": "slot_hand", "type": "slot" },
              { "id": "item_cursed", "type": "item", "slot": "slot_hand",
                "equipEffects": [ { "type": "PeriodicEffect", "interval": 1, "effects": [] } ] } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("may not contain PeriodicEffect"));
    }

    [Fact]
    public void UnknownEffectType_IsError()
    {
        _host.WriteContent("bad-effect.json", """
            { "id": "item_a", "type": "item",
              "useActions": [ { "type": "Explode", "formula": "1" } ] }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("unknown effect type 'Explode'"));
    }

    [Fact]
    public void ShopFormulaWithUnknownIdentifier_IsError()
    {
        _host.WriteContent("shop.json", """
            [ { "id": "item_gold", "type": "item" },
              { "id": "shop_x", "type": "shop", "buyPriceFormula": "BasePrice * Luck" } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("unknown identifier 'Luck'"));
    }
}
