namespace Lattice.Narrative.Tests;

public sealed class YarnDialogueTests : IDisposable
{
    private readonly NarrativeTestHost _host = new();

    public YarnDialogueTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void LinesAndSpeakers_FlowThroughAdvance()
    {
        _host.WriteContent("dialogue/test.yarn", """
            title: Test
            ---
            Greeter: Hello there.
            Greeter: Second line.
            ===
            """);
        var (_, _, narrative) = _host.CreateLoadedSession();

        Assert.True(narrative.Dialogue.StartYarn("Test", out _));
        Assert.Equal(DialogueState.Line, narrative.Dialogue.State);
        Assert.Equal("Greeter", narrative.Dialogue.Speaker);
        Assert.Equal("Hello there.", narrative.Dialogue.Line);

        narrative.Dialogue.Advance();
        Assert.Equal("Second line.", narrative.Dialogue.Line);

        narrative.Dialogue.Advance();
        Assert.Equal(DialogueState.Ended, narrative.Dialogue.State);
    }

    [Fact]
    public void Options_PresentAndBranch()
    {
        _host.WriteContent("dialogue/test.yarn", """
            title: Test
            ---
            Greeter: Pick one.
            -> Left
                Greeter: You went left.
            -> Right
                Greeter: You went right.
            ===
            """);
        var (_, _, narrative) = _host.CreateLoadedSession();
        narrative.Dialogue.StartYarn("Test", out _);
        narrative.Dialogue.Advance();

        Assert.Equal(DialogueState.Options, narrative.Dialogue.State);
        Assert.Equal(["Left", "Right"], narrative.Dialogue.Options.Select(o => o.Text));

        narrative.Dialogue.Choose(narrative.Dialogue.Options[1].Id);
        Assert.Equal("You went right.", narrative.Dialogue.Line);
    }

    [Fact]
    public void Variables_AreBlackboardFlags_BothWays()
    {
        _host.WriteContent("dialogue/test.yarn", """
            title: Test
            ---
            <<declare $visits = 0>>
            <<set $visits = $visits + 1>>
            Greeter: Visit number {$visits}.
            ===
            """);
        var (session, _, narrative) = _host.CreateLoadedSession();
        session.Flags.Write("visits", 4.0); // pre-existing world state visible to Yarn

        narrative.Dialogue.StartYarn("Test", out _);

        Assert.Equal("Visit number 5.", narrative.Dialogue.Line);
        Assert.Equal(5.0, session.Flags.ReadNumber("visits")); // Yarn write landed on the blackboard
    }

    [Fact]
    public void Commands_BridgeToEffects()
    {
        _host.WriteContent("dialogue/test.yarn", """
            title: Test
            ---
            Greeter: Take this.
            <<give item_gold 7>>
            <<flag met_greeter true>>
            <<start_quest quest_wolves>>
            Greeter: Done.
            ===
            """);
        var (session, rpg, narrative) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));

        narrative.Dialogue.StartYarn("Test", out _);
        narrative.Dialogue.Advance(); // past "Take this." — commands run, then "Done."

        Assert.Equal("Done.", narrative.Dialogue.Line);
        Assert.Equal(37, rpg.CountItem(player, "item_gold"));
        Assert.True(session.Flags.ReadBool("met_greeter"));
        Assert.Equal(QuestStatus.Active, narrative.Quests.GetStatus("quest_wolves"));
    }

    [Fact]
    public void RunCommand_MarshalsJsonToAnyEffectPrimitive()
    {
        var (session, _, narrative) = _host.CreateLoadedSession();

        narrative.HandleDialogueCommand("""run {"type":"SetFlag","flag":"ran_json","value":true}""");

        Assert.True(session.Flags.ReadBool("ran_json"));
    }

    [Fact]
    public void Functions_ReadWorldState()
    {
        _host.WriteContent("dialogue/test.yarn", """
            title: Test
            ---
            <<if has_item("item_gold") and flag_number("wolves_killed") >= 2 and quest_active("quest_wolves")>>
                Greeter: All true.
            <<else>>
                Greeter: Something false.
            <<endif>>
            ===
            """);
        var (session, _, narrative) = _host.CreateLoadedSession();
        session.Flags.Write("wolves_killed", 2.0);
        narrative.Quests.Start("quest_wolves");

        narrative.Dialogue.StartYarn("Test", out _);

        Assert.Equal("All true.", narrative.Dialogue.Line);
    }

    [Fact]
    public void MissingNode_FailsCleanly()
    {
        var (_, _, narrative) = _host.CreateLoadedSession();

        Assert.False(narrative.Dialogue.StartYarn("Nope", out var error));
        Assert.Contains("Nope", error);
    }
}
