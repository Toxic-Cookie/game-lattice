using Lattice.Core.Content;

namespace Lattice.Core.Tests.Content;

public sealed class HotReloadTests : IDisposable
{
    private readonly TestHost _host = new(watch: true);

    public void Dispose() => _host.Dispose();

    private static async Task PumpUntil(Func<bool> condition, Lattice.Core.Simulation.GameSession session, double timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            session.AdvanceTick(1f / 30f);
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task EditedDef_IsSwappedLive_AndEventPublished()
    {
        _host.WriteContent("wolf.json", """{ "id": "entity_wolf", "type": "entity", "stats": { "HP": 12 } }""");
        var session = _host.CreateSession();
        Assert.True(session.LoadContent().Ok);
        session.EnableHotReload(debounceSeconds: 0.05);

        string? reloadedIds = null;
        session.Events.Subscribe("Content.Reloaded", e => reloadedIds = e.Payload["defIds"] as string);

        _host.WriteContent("wolf.json", """{ "id": "entity_wolf", "type": "entity", "stats": { "HP": 99 } }""");
        await PumpUntil(() => reloadedIds is not null, session);

        Assert.Equal("entity_wolf", reloadedIds);
        Assert.Equal(99, session.Defs.Get<EntityTemplateDef>("entity_wolf").Stats!["HP"]);
    }

    [Fact]
    public async Task BrokenEdit_KeepsOldDefs()
    {
        _host.WriteContent("wolf.json", """{ "id": "entity_wolf", "type": "entity", "stats": { "HP": 12 } }""");
        var session = _host.CreateSession();
        session.LoadContent();
        session.EnableHotReload(debounceSeconds: 0.05);

        _host.WriteContent("wolf.json", "{ this is not json");

        // pump generously; the reload must be attempted and rejected
        var attempted = false;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !attempted)
        {
            session.AdvanceTick(1f / 30f);
            attempted = !session.Defs.Contains("entity_wolf") ||
                        session.Defs.Get<EntityTemplateDef>("entity_wolf").Stats!["HP"] != 12;
            await Task.Delay(25);
        }

        Assert.True(session.Defs.Contains("entity_wolf"));
        Assert.Equal(12, session.Defs.Get<EntityTemplateDef>("entity_wolf").Stats!["HP"]);
    }

    [Fact]
    public async Task NewFile_AddsDefs_DeletedFile_RemovesThem()
    {
        var session = _host.CreateSession();
        session.LoadContent();
        session.EnableHotReload(debounceSeconds: 0.05);

        _host.WriteContent("new.json", """{ "id": "entity_new", "type": "entity" }""");
        await PumpUntil(() => session.Defs.Contains("entity_new"), session);
        Assert.True(session.Defs.Contains("entity_new"));

        File.Delete(Path.Combine(_host.ContentRoot, "new.json"));
        await PumpUntil(() => !session.Defs.Contains("entity_new"), session);
        Assert.False(session.Defs.Contains("entity_new"));
    }
}
