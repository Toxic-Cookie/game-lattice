using Lattice.Core.Formulas;
using Lattice.Core.Simulation;

namespace Lattice.Core.Tests.Formulas;

public class NCalcFormulaEngineTests
{
    private sealed class DictContext(Dictionary<string, double> values) : IFormulaContext
    {
        public bool TryResolve(string identifier, out double value) => values.TryGetValue(identifier, out value);
    }

    private static NCalcFormulaEngine Engine(int seed = 1) => new(new LatticeRandom(seed));

    [Theory]
    [InlineData("1 + 2", 3)]
    [InlineData("2 * (3 + 4)", 14)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("2 > 1", 1)] // booleans coerce to 0/1
    public void Evaluate_Arithmetic(string formula, double expected)
    {
        Assert.Equal(expected, Engine().Evaluate(formula), precision: 10);
    }

    [Fact]
    public void Evaluate_ResolvesIdentifiersThroughContext()
    {
        var ctx = new DictContext(new() { ["Str"] = 8, ["Level"] = 3 });

        Assert.Equal(19, Engine().Evaluate("(Str * 2) + Level", ctx));
    }

    [Fact]
    public void Evaluate_UnknownIdentifier_ThrowsNamingFormulaAndIdentifier()
    {
        var ex = Assert.Throws<FormulaException>(() => Engine().Evaluate("Str + 1"));

        Assert.Contains("Str", ex.Message);
        Assert.Contains("Str + 1", ex.Message);
    }

    [Fact]
    public void Evaluate_CompositeContext_FirstResolverWins()
    {
        var entity = new DictContext(new() { ["HP"] = 12 });
        var globals = new DictContext(new() { ["HP"] = 999, ["WorldLevel"] = 5 });
        var chain = new CompositeFormulaContext(entity, globals);

        Assert.Equal(12, Engine().Evaluate("HP", chain));
        Assert.Equal(5, Engine().Evaluate("WorldLevel", chain));
    }

    [Fact]
    public void Dice_RollsAreInRangeAndDeterministic()
    {
        var a = Engine(seed: 42);
        var b = Engine(seed: 42);

        for (var i = 0; i < 50; i++)
        {
            var roll = a.Evaluate("1d10+5");
            Assert.InRange(roll, 6, 15);
            Assert.Equal(roll, b.Evaluate("1d10+5"));
        }
    }

    [Fact]
    public void Dice_MultipleDiceSumCorrectRange()
    {
        var engine = Engine(seed: 7);
        for (var i = 0; i < 50; i++)
        {
            Assert.InRange(engine.Evaluate("3d6"), 3, 18);
        }
    }

    [Fact]
    public void TryParse_AcceptsValidFormulas()
    {
        Assert.True(Engine().TryParse("(Str * 2) + dice(1, 6)", out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("((2)")]
    public void TryParse_RejectsBrokenSyntax(string formula)
    {
        Assert.False(Engine().TryParse(formula, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Evaluate_SameFormulaTwice_UsesCache()
    {
        var engine = Engine();
        // correctness proxy for the cache: identical results, no exceptions on re-eval
        Assert.Equal(engine.Evaluate("2 + 2"), engine.Evaluate("2 + 2"));
    }
}
