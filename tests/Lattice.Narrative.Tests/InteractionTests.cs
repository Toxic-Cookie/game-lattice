namespace Lattice.Narrative.Tests;

public sealed class InteractionTests : IDisposable
{
    private readonly NarrativeTestHost _host = new();

    public InteractionTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Interact_RunsEffects_OncePerFlagGate()
    {
        var (session, rpg, narrative) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var chest = session.World.All.Single(e => e.DefId == "entity_chest");
        var goldBefore = rpg.CountItem(player, "item_gold");

        Assert.True(narrative.Interactions.TryInteract(player, chest, "open", out _));
        session.Events.DispatchPending();

        var gained = rpg.CountItem(player, "item_gold") - goldBefore;
        Assert.InRange(gained, 2, 12); // 2d6
        Assert.True(session.Flags.ReadBool("chest_looted"));

        Assert.False(narrative.Interactions.TryInteract(player, chest, "open", out var error));
        Assert.Contains("conditions", error);
    }

    [Fact]
    public void UnknownVerbAndNonObjects_FailWithReason()
    {
        var (session, _, narrative) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var chest = session.World.All.Single(e => e.DefId == "entity_chest");

        Assert.False(narrative.Interactions.TryInteract(player, chest, "kick", out var error));
        Assert.Contains("no 'kick' interaction", error);

        Assert.False(narrative.Interactions.TryInteract(player, player, "open", out error));
        Assert.Contains("not a smart object", error);
    }

    [Fact]
    public void Reservation_HonorsMaxUsers()
    {
        var (session, _, narrative) = _host.CreateLoadedSession();
        var chest = session.World.All.Single(e => e.DefId == "entity_chest");

        Assert.True(narrative.Interactions.TryReserve(chest, "agent_a"));
        Assert.True(narrative.Interactions.TryReserve(chest, "agent_a")); // idempotent
        Assert.False(narrative.Interactions.TryReserve(chest, "agent_b")); // maxUsers 1

        narrative.Interactions.Release(chest, "agent_a");
        Assert.True(narrative.Interactions.TryReserve(chest, "agent_b"));
    }

    [Fact]
    public void Interaction_PublishesEvent()
    {
        var (session, _, narrative) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var chest = session.World.All.Single(e => e.DefId == "entity_chest");
        string? observedVerb = null;
        session.Events.Subscribe("Interaction.Performed", e => observedVerb = e.Payload["verb"] as string);

        narrative.Interactions.TryInteract(player, chest, "open", out _);
        session.Events.DispatchPending();

        Assert.Equal("open", observedVerb);
    }
}
