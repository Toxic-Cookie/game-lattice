using Lattice.Core.Events;

namespace Lattice.Core.Tests.Events;

public class EventBusTests
{
    [Fact]
    public void Publish_IsDeferredUntilDispatch()
    {
        var bus = new EventBus();
        var received = new List<string>();
        bus.Subscribe("Test.Topic", e => received.Add(e.Topic));

        bus.Publish("Test.Topic");
        Assert.Empty(received);

        bus.DispatchPending();
        Assert.Equal(["Test.Topic"], received);
    }

    [Fact]
    public void WildcardSubscription_MatchesPrefix()
    {
        var bus = new EventBus();
        var received = new List<string>();
        bus.Subscribe("Player*", e => received.Add(e.Topic));

        bus.Publish("PlayerKilledWolf");
        bus.Publish("Player.Moved");
        bus.Publish("Quest.Started");
        bus.DispatchPending();

        Assert.Equal(["PlayerKilledWolf", "Player.Moved"], received);
    }

    [Fact]
    public void EventsPublishedByHandlers_AreDeliveredInSameDispatch()
    {
        var bus = new EventBus();
        var received = new List<string>();
        bus.Subscribe("First", _ => bus.Publish("Second"));
        bus.Subscribe("Second", e => received.Add(e.Topic));

        bus.Publish("First");
        bus.DispatchPending();

        Assert.Equal(["Second"], received);
    }

    [Fact]
    public void CascadeLimit_StopsRunawayFeedback()
    {
        var bus = new EventBus();
        bus.Subscribe("Loop", _ => bus.Publish("Loop"));

        bus.Publish("Loop");
        var delivered = bus.DispatchPending(cascadeLimit: 50);

        Assert.Equal(50, delivered);
        Assert.Equal(0, bus.PendingCount);
    }

    [Fact]
    public void ScopedBus_BubblesToParent()
    {
        var global = new EventBus();
        var scope = global.CreateScope();
        var globalSeen = new List<string>();
        var scopeSeen = new List<string>();
        global.Subscribe("Scene.Thing", e => globalSeen.Add(e.Topic));
        scope.Subscribe("Scene.Thing", e => scopeSeen.Add(e.Topic));

        scope.Publish("Scene.Thing");
        scope.DispatchPending();
        global.DispatchPending();

        Assert.Single(scopeSeen);
        Assert.Single(globalSeen);
    }

    [Fact]
    public void Unsubscribe_StopsDelivery()
    {
        var bus = new EventBus();
        var count = 0;
        var subscription = bus.Subscribe("T", _ => count++);

        bus.Publish("T");
        bus.DispatchPending();
        subscription.Dispose();
        bus.Publish("T");
        bus.DispatchPending();

        Assert.Equal(1, count);
    }

    [Fact]
    public void HandlerException_DoesNotBlockOtherHandlers()
    {
        var bus = new EventBus();
        var reached = false;
        bus.Subscribe("T", _ => throw new InvalidOperationException("boom"));
        bus.Subscribe("T", _ => reached = true);

        bus.Publish("T");
        bus.DispatchPending();

        Assert.True(reached);
    }

    [Fact]
    public void Trace_KeepsDispatchedEvents()
    {
        var bus = new EventBus(traceCapacity: 2);
        bus.Publish("A");
        bus.Publish("B");
        bus.Publish("C");
        bus.DispatchPending();

        Assert.Equal(["B", "C"], bus.Trace.Select(e => e.Topic));
    }
}
