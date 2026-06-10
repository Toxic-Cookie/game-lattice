using Lattice.Core.Hosting.Standalone;

namespace Lattice.Core.Tests.Hosting;

public class TimedStubAnimationServiceTests
{
    [Fact]
    public void Play_ThenQuery_ReportsPlayingUntilDurationElapses()
    {
        var anim = new TimedStubAnimationService(animationDurationSeconds: 1.0);

        anim.Play("npc_1", "attack");

        Assert.True(anim.IsPlaying("npc_1", "attack"));
        Assert.False(anim.IsComplete("npc_1", "attack"));

        anim.Advance(1.5);

        Assert.False(anim.IsPlaying("npc_1", "attack"));
        Assert.True(anim.IsComplete("npc_1", "attack"));
    }

    [Fact]
    public void NonInterruptible_IsReportedWhileRunningOnly()
    {
        var anim = new TimedStubAnimationService(animationDurationSeconds: 1.0);

        anim.Play("npc_1", "grenade_throw", interruptible: false);
        Assert.True(anim.IsPlayingNonInterruptible("npc_1"));

        anim.Advance(2.0);
        Assert.False(anim.IsPlayingNonInterruptible("npc_1"));
    }

    [Fact]
    public void Play_ReplacesPreviousAnimation()
    {
        var anim = new TimedStubAnimationService(animationDurationSeconds: 1.0);

        anim.Play("npc_1", "idle");
        anim.Play("npc_1", "attack");

        Assert.False(anim.IsPlaying("npc_1", "idle"));
        Assert.True(anim.IsPlaying("npc_1", "attack"));
    }

    [Fact]
    public void Stop_ClearsState()
    {
        var anim = new TimedStubAnimationService(animationDurationSeconds: 1.0);

        anim.Play("npc_1", "idle", interruptible: false);
        anim.Stop("npc_1");

        Assert.False(anim.IsPlaying("npc_1", "idle"));
        Assert.False(anim.IsPlayingNonInterruptible("npc_1"));
    }
}
