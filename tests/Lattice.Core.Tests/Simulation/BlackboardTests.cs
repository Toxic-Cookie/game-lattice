using Lattice.Core.Simulation;

namespace Lattice.Core.Tests.Simulation;

public class BlackboardTests
{
    [Fact]
    public void WriteAndRead_RoundTripsScalars()
    {
        var bb = new Blackboard();
        bb.Write("flag", true);
        bb.Write("count", 3);
        bb.Write("name", "wolf");

        Assert.True(bb.ReadBool("flag"));
        Assert.Equal(3.0, bb.ReadNumber("count")); // ints coerce to double
        Assert.Equal("wolf", bb.ReadString("name"));
    }

    [Fact]
    public void Write_RejectsNonScalarValues()
    {
        var bb = new Blackboard();

        Assert.Throws<ArgumentException>(() => bb.Write("bad", new object()));
    }

    [Fact]
    public void ReadWithAge_TracksClock()
    {
        var now = 0.0;
        var bb = new Blackboard(() => now);

        bb.Write("threat", true);
        now = 2.5;

        var (value, age) = bb.ReadWithAge("threat");
        Assert.Equal(true, value);
        Assert.Equal(2.5, age, precision: 10);
    }

    [Fact]
    public void IsStale_RespectsThresholdAndMissingKeys()
    {
        var now = 0.0;
        var bb = new Blackboard(() => now);
        bb.Write("threat", true);

        now = 2.0;
        Assert.False(bb.IsStale("threat", maxAgeSeconds: 3.0));

        now = 4.0;
        Assert.True(bb.IsStale("threat", maxAgeSeconds: 3.0));
        Assert.True(bb.IsStale("never_written", maxAgeSeconds: 100.0));
    }

    [Fact]
    public void Subscribe_NotifiesOnWrite_UntilDisposed()
    {
        var bb = new Blackboard();
        var seen = new List<object?>();
        var sub = bb.Subscribe("hp", (_, value) => seen.Add(value));

        bb.Write("hp", 10);
        sub.Dispose();
        bb.Write("hp", 5);

        Assert.Equal([10.0], seen);
    }

    [Fact]
    public void Export_SnapshotsAllEntries()
    {
        var bb = new Blackboard();
        bb.Write("a", 1);
        bb.Write("b", "x");

        var export = bb.Export();

        Assert.Equal(2, export.Count);
        Assert.Equal(1.0, export["a"]);
    }
}
