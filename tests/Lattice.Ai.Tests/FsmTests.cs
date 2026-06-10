using Lattice.Ai.Fsm;

namespace Lattice.Ai.Tests;

/// <summary>Generic FSM/StackFSM behavior, including the ant interruption scenario from the research (fsm-theory Part 8).</summary>
public class FsmTests
{
    private sealed class Ant
    {
        public StackFsm<Ant>? Brain;

        public List<string> Log { get; } = [];
    }

    [Fact]
    public void SimpleFsm_RunsLifecycleHooks()
    {
        var log = new List<string>();
        var owner = new object();
        var fsm = new Fsm<object>(owner);
        var a = new FsmState<object>("a", _ => log.Add("tick-a"), _ => log.Add("enter-a"), _ => log.Add("exit-a"));
        var b = new FsmState<object>("b", _ => log.Add("tick-b"), _ => log.Add("enter-b"));

        fsm.SetState(a);
        fsm.Update();
        fsm.SetState(b);
        fsm.Update();

        Assert.Equal(["enter-a", "tick-a", "exit-a", "enter-b", "tick-b"], log);
    }

    [Fact]
    public void SimpleFsm_SettingSameState_IsNoOp()
    {
        var entries = 0;
        var fsm = new Fsm<object>(new object());
        var state = new FsmState<object>("s", _ => { }, _ => entries++);

        fsm.SetState(state);
        fsm.SetState(state);

        Assert.Equal(1, entries);
    }

    [Fact]
    public void StackFsm_RePushGuard_PreventsUnboundedGrowth()
    {
        var fsm = new StackFsm<object>(new object());
        var state = new FsmState<object>("flee", _ => { });

        for (var i = 0; i < 100; i++)
        {
            fsm.Push(state); // a threat condition firing every frame
        }

        Assert.Equal(1, fsm.Depth);
    }

    [Fact]
    public void StackFsm_AntScenario_HistoryDeterminesResume()
    {
        // two ants with identical rules but different stack histories
        // respond to the same interruption differently (research ch02 §2.6)
        var findLeaf = new FsmState<Ant>("findLeaf", a => a.Log.Add("findLeaf"));
        var goHome = new FsmState<Ant>("goHome", a => a.Log.Add("goHome"));
        var runAway = new FsmState<Ant>("runAway", a => a.Log.Add("runAway"));

        var ant1 = new Ant();
        ant1.Brain = new StackFsm<Ant>(ant1);
        ant1.Brain.Push(findLeaf);

        var ant2 = new Ant();
        ant2.Brain = new StackFsm<Ant>(ant2);
        ant2.Brain.Push(goHome);

        // mouse appears: both flee
        ant1.Brain.Push(runAway);
        ant2.Brain.Push(runAway);
        Assert.Equal("runAway", ant1.Brain.Current!.Name);
        Assert.Equal("runAway", ant2.Brain.Current!.Name);

        // mouse leaves: both pop — and resume *different* states
        ant1.Brain.Pop();
        ant2.Brain.Pop();
        Assert.Equal("findLeaf", ant1.Brain.Current!.Name);
        Assert.Equal("goHome", ant2.Brain.Current!.Name);
    }

    [Fact]
    public void StackFsm_Replace_SwapsTopWithoutRestoring()
    {
        var fsm = new StackFsm<object>(new object());
        var bottom = new FsmState<object>("bottom", _ => { });
        var a = new FsmState<object>("a", _ => { });
        var b = new FsmState<object>("b", _ => { });

        fsm.Push(bottom);
        fsm.Push(a);
        fsm.Replace(b);

        Assert.Equal(["bottom", "b"], fsm.StackNames);
    }
}
