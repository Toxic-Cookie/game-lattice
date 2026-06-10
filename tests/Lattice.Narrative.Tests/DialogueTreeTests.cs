namespace Lattice.Narrative.Tests;

public sealed class DialogueTreeTests : IDisposable
{
    private readonly NarrativeTestHost _host = new();

    public DialogueTreeTests()
    {
        _host.WriteStandardContent();
        _host.WriteContent("tree.json", """
            { "id": "tree_guard", "type": "dialogue", "start": "hello",
              "nodes": {
                "hello": { "speaker": "Guard", "line": "Halt.", "options": [
                    { "text": "Just visiting.", "next": "visit" },
                    { "text": "[Bribe 10 gold]",
                      "conditions": [ { "type": "HasItem", "item": "item_gold", "count": 10 } ],
                      "effects": [ { "type": "RemoveItem", "item": "item_gold", "amount": "10" } ],
                      "next": "bribe" } ] },
                "visit": { "speaker": "Guard", "line": "Move along.", "next": "post" },
                "bribe": { "speaker": "Guard", "line": "I saw nothing.",
                           "effects": [ { "type": "SetFlag", "flag": "guard_bribed", "value": true } ] },
                "post": { "speaker": "Guard", "line": "And stay out of trouble." }
              } }
            """);
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Options_FilterByConditions_AndRunEffects()
    {
        var (session, rpg, narrative) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));

        Assert.True(narrative.Dialogue.StartTree("tree_guard", out _));
        Assert.Equal("Halt.", narrative.Dialogue.Line);
        narrative.Dialogue.Advance();

        Assert.Equal(2, narrative.Dialogue.Options.Count); // 30 gold -> bribe eligible
        narrative.Dialogue.Choose(narrative.Dialogue.Options[1].Id);

        Assert.Equal("I saw nothing.", narrative.Dialogue.Line);
        Assert.Equal(20, rpg.CountItem(player, "item_gold"));
        Assert.True(session.Flags.ReadBool("guard_bribed"));

        narrative.Dialogue.Advance();
        Assert.Equal(DialogueState.Ended, narrative.Dialogue.State);
    }

    [Fact]
    public void BribeOption_HiddenWithoutGold()
    {
        var (session, rpg, narrative) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        rpg.RemoveItem(player, "item_gold", 25); // leaves 5

        narrative.Dialogue.StartTree("tree_guard", out _);
        narrative.Dialogue.Advance();

        Assert.Single(narrative.Dialogue.Options);
        Assert.Equal("Just visiting.", narrative.Dialogue.Options[0].Text);
    }

    [Fact]
    public void NextChaining_AutoAdvancesThroughNodes()
    {
        var (_, _, narrative) = _host.CreateLoadedSession();

        narrative.Dialogue.StartTree("tree_guard", out _);
        narrative.Dialogue.Advance();
        narrative.Dialogue.Choose(narrative.Dialogue.Options[0].Id); // visit -> next: post

        Assert.Equal("Move along.", narrative.Dialogue.Line);
        narrative.Dialogue.Advance();
        Assert.Equal("And stay out of trouble.", narrative.Dialogue.Line);
        narrative.Dialogue.Advance();
        Assert.Equal(DialogueState.Ended, narrative.Dialogue.State);
    }
}
