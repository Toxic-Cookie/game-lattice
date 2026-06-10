using Lattice.Core.Simulation;

namespace Lattice.Core.Tests.Simulation;

public class LatticeRandomTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new LatticeRandom(123);
        var b = new LatticeRandom(123);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt(), b.NextUInt());
        }
    }

    [Fact]
    public void StateRoundTrip_ResumesSequence()
    {
        var original = new LatticeRandom(9);
        original.NextUInt();
        var state = original.State;

        var resumed = new LatticeRandom(0) { State = state };

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(original.NextUInt(), resumed.NextUInt());
        }
    }

    [Fact]
    public void NextInt_StaysInRange()
    {
        var rng = new LatticeRandom(5);
        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(rng.NextInt(3, 10), 3, 9);
        }
    }

    [Fact]
    public void NextDouble_StaysInUnitInterval()
    {
        var rng = new LatticeRandom(5);
        for (var i = 0; i < 1000; i++)
        {
            var value = rng.NextDouble();
            Assert.True(value is >= 0 and < 1);
        }
    }

    [Fact]
    public void RollDice_StaysInRange()
    {
        var rng = new LatticeRandom(5);
        for (var i = 0; i < 200; i++)
        {
            Assert.InRange(rng.RollDice(2, 6), 2, 12);
        }
    }
}
