using System.Numerics;
using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;

namespace Lattice.Core.Tests.Hosting;

public class StraightLineNavigationServiceTests
{
    private static readonly Vector3 Node = new(3, 0, 4);

    [Fact]
    public void TryFindPath_ReturnsEndpoints()
    {
        var nav = new StraightLineNavigationService();
        var waypoints = new List<Vector3> { new(9, 9, 9) }; // pre-existing junk must be cleared

        var found = nav.TryFindPath(Vector3.Zero, Node, NavQueryContext.Default, waypoints);

        Assert.True(found);
        Assert.Equal([Vector3.Zero, Node], waypoints);
    }

    [Fact]
    public void ReserveNode_ExcludesOtherAgents()
    {
        var nav = new StraightLineNavigationService();

        Assert.True(nav.TryReserveNode(Node, "agent_a"));
        Assert.False(nav.TryReserveNode(Node, "agent_b"));
    }

    [Fact]
    public void ReserveNode_IsIdempotentForHolder()
    {
        var nav = new StraightLineNavigationService();

        Assert.True(nav.TryReserveNode(Node, "agent_a"));
        Assert.True(nav.TryReserveNode(Node, "agent_a"));
    }

    [Fact]
    public void ReleaseNode_ByNonHolder_DoesNotRelease()
    {
        var nav = new StraightLineNavigationService();
        nav.TryReserveNode(Node, "agent_a");

        nav.ReleaseNode(Node, "agent_b");

        Assert.False(nav.TryReserveNode(Node, "agent_b"));
    }

    [Fact]
    public void ReleaseNode_ByHolder_FreesNode()
    {
        var nav = new StraightLineNavigationService();
        nav.TryReserveNode(Node, "agent_a");

        nav.ReleaseNode(Node, "agent_a");

        Assert.True(nav.TryReserveNode(Node, "agent_b"));
    }
}
