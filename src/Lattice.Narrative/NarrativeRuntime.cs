using System.Text.Json;
using Lattice.Core.Content;
using Lattice.Core.Hosting;
using Lattice.Core.Simulation;
using Lattice.Narrative.Defs;
using Lattice.Narrative.Yarn;
using Lattice.Rpg;
using YarnLibrary = Yarn.Library;

namespace Lattice.Narrative;

/// <summary>
/// The narrative module (plan/03): Yarn + JSON-tree dialogue, the quest
/// engine, and smart-object interactions. Attach after the RPG module and
/// before <see cref="GameSession.LoadContent"/>.
/// </summary>
public sealed class NarrativeRuntime
{
    private bool _yarnDirty;
    private double _yarnDirtyAt;

    internal NarrativeRuntime(GameSession session, RpgRuntime rpg)
    {
        Session = session;
        Rpg = rpg;
        Yarn = new YarnScriptManager();
        Dialogue = new DialogueRunner(this);
        Quests = new QuestService(this);
        Interactions = new InteractionService(this);

        session.RegisterModule(this);
        rpg.Effects.Register(new StartQuestEffect());
        session.ContentLoaded += _ => OnContentChanged();
        session.Events.Subscribe("*", Quests.OnAnyEvent);
        session.Services.Content.Changed += OnContentFileChanged;
        session.RegisterSystem(new NarrativeSystem(this));
        session.RegisterSaveSection(new NarrativeSaveSection(this));
        session.RegisterContentValidator(new NarrativeContentValidator(rpg.Effects, rpg.Conditions));
    }

    public GameSession Session { get; }

    public RpgRuntime Rpg { get; }

    public YarnScriptManager Yarn { get; }

    public DialogueRunner Dialogue { get; }

    public QuestService Quests { get; }

    public InteractionService Interactions { get; }

    /// <summary>
    /// Yarn functions exposed to scripts (also passed to the compiler so
    /// scripts type-check). Keep signatures in sync with
    /// <see cref="CreateCompilationLibrary"/>.
    /// </summary>
    internal YarnLibrary BuildFunctionLibrary()
    {
        var library = new YarnLibrary();
        library.RegisterFunction("flag_bool", (string key) => Session.Flags.ReadBool(key));
        library.RegisterFunction("flag_number", (string key) => (float)Session.Flags.ReadNumber(key));
        library.RegisterFunction("has_item", (string itemId) =>
            Dialogue.Player is { } player && Rpg.CountItem(player, itemId) >= 1);
        library.RegisterFunction("stat", (string key) =>
            Dialogue.Player is { } player && Rpg.GetSheet(player) is { } sheet && sheet.HasStat(key)
                ? (float)sheet.Current(key)
                : 0f);
        library.RegisterFunction("quest_active", (string questId) => Quests.GetStatus(questId) == QuestStatus.Active);
        library.RegisterFunction("quest_completed", (string questId) => Quests.GetStatus(questId) == QuestStatus.Completed);
        return library;
    }

    /// <summary>Compile-only stub library for tooling (`lattice validate`): same names and signatures, no runtime.</summary>
    public static YarnLibrary CreateCompilationLibrary()
    {
        var library = new YarnLibrary();
        library.RegisterFunction("flag_bool", (string _) => false);
        library.RegisterFunction("flag_number", (string _) => 0f);
        library.RegisterFunction("has_item", (string _) => false);
        library.RegisterFunction("stat", (string _) => 0f);
        library.RegisterFunction("quest_active", (string _) => false);
        library.RegisterFunction("quest_completed", (string _) => false);
        return library;
    }

    /// <summary>
    /// Dialogue command bridge (plan/03 §2): <c>run {json}</c> marshals to
    /// any effect primitive; the rest are sugar. Public because hosts
    /// (cutscene systems, debug consoles) may drive it directly. Note: in
    /// .yarn files, JSON braces inside <c>&lt;&lt;run ...&gt;&gt;</c> must be
    /// escaped (<c>\{</c>) because Yarn treats <c>{}</c> as interpolation —
    /// prefer the sugar commands in hand-written scripts.
    /// </summary>
    public void HandleDialogueCommand(string commandText)
    {
        var logger = Session.Services.Host.Logger;
        var split = commandText.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (split.Length == 0)
        {
            return;
        }

        var rest = split.Length > 1 ? split[1].Trim() : "";
        switch (split[0])
        {
            case "run":
                try
                {
                    using var doc = JsonDocument.Parse(rest);
                    Rpg.RunEffects([doc.RootElement.Clone()], source: null, target: Dialogue.Player);
                }
                catch (JsonException ex)
                {
                    logger.Error($"Dialogue 'run' command has invalid JSON: {ex.Message}");
                }

                break;

            case "give":
            {
                var args = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length >= 1 && Dialogue.Player is { } player)
                {
                    var amount = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 1;
                    Rpg.GiveItem(player, args[0], amount);
                }

                break;
            }

            case "flag":
            {
                var args = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (args.Length == 2)
                {
                    Session.Flags.Write(args[0], JsonValueHelper.ParseLiteral(args[1]));
                }

                break;
            }

            case "start_quest":
                Quests.Start(rest);
                break;

            case "publish":
                Session.Events.Publish(rest, tick: Session.Tick);
                break;

            default:
                logger.Warning($"Unknown dialogue command '{split[0]}'.");
                break;
        }
    }

    private void OnContentChanged()
    {
        Yarn.Compile(Session.Services.Content, BuildFunctionLibrary(), Session.Services.Host.Logger);
        Interactions.RebuildIndex(Session.Defs);
    }

    private void OnContentFileChanged(ContentChange change)
    {
        if (change.File.RelativePath.EndsWith(".yarn", StringComparison.OrdinalIgnoreCase))
        {
            _yarnDirty = true;
            _yarnDirtyAt = Session.Services.Host.WallClockSeconds;
        }
    }

    /// <summary>Per-tick work: debounced Yarn hot-recompile + quest progression checks.</summary>
    private sealed class NarrativeSystem(NarrativeRuntime narrative) : ISimSystem
    {
        public string Name => "narrative";

        public void Tick(GameSession session, float dt)
        {
            if (narrative._yarnDirty
                && session.Services.Host.WallClockSeconds - narrative._yarnDirtyAt >= 0.25)
            {
                narrative._yarnDirty = false;
                narrative.Yarn.Compile(session.Services.Content, narrative.BuildFunctionLibrary(), session.Services.Host.Logger);
            }

            narrative.Quests.CheckProgress();
        }
    }
}

/// <summary>Entry points for wiring the narrative module into a session.</summary>
public static class LatticeNarrative
{
    /// <summary>RPG def kinds plus the narrative vocabulary.</summary>
    public static DefTypeRegistry CreateDefTypes()
    {
        var types = LatticeRpg.CreateDefTypes();
        types.Register<DialogueTreeDef>("dialogue");
        types.Register<QuestDef>("quest");
        types.Register<SmartObjectDef>("smartobject");
        return types;
    }

    /// <summary>Attach the narrative module. Call after <see cref="LatticeRpg.Attach"/>, before LoadContent.</summary>
    public static NarrativeRuntime Attach(GameSession session, RpgRuntime rpg) => new(session, rpg);
}
