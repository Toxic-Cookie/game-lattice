using System.Text.Json;
using Lattice.Core.Content;
using Lattice.Core.Events;
using Lattice.Core.Hosting;
using Lattice.Core.Simulation;
using Lattice.Narrative.Defs;
using Lattice.Rpg.Conditions;
using Yarn;

namespace Lattice.Narrative;

public enum DialogueState
{
    Idle,
    Line,
    Options,
    Ended,
}

/// <summary>One selectable dialogue option.</summary>
/// <param name="Id">Pass back to <see cref="DialogueRunner.Choose"/>.</param>
public sealed record DialogueOption(int Id, string Text);

/// <summary>
/// Pull-based conversation driver serving both Yarn scripts and JSON
/// dialogue trees — callers can't tell which backend served the
/// conversation. The host (console loop, engine UI) polls
/// <see cref="State"/>/<see cref="Line"/>/<see cref="Options"/> and calls
/// <see cref="Advance"/>/<see cref="Choose"/>; bus events mirror every
/// transition for event-driven UIs. One conversation at a time; dialogue
/// state is not saved (conversations don't survive save/load, v1).
/// </summary>
public sealed class DialogueRunner
{
    private readonly NarrativeRuntime _narrative;
    private Dialogue? _yarn;
    private bool _yarnDelivered;
    private bool _yarnCommandHandled;
    private bool _yarnActive;
    private TreeCursor? _tree;

    internal DialogueRunner(NarrativeRuntime narrative)
    {
        _narrative = narrative;
    }

    public DialogueState State { get; private set; } = DialogueState.Idle;

    public string? Speaker { get; private set; }

    public string? Line { get; private set; }

    public IReadOnlyList<DialogueOption> Options { get; private set; } = [];

    /// <summary>Start a Yarn conversation at a node.</summary>
    public bool StartYarn(string nodeName, out string? error)
    {
        error = null;
        if (_narrative.Yarn.Program is null || !_narrative.Yarn.NodeExists(nodeName))
        {
            error = $"Yarn node '{nodeName}' does not exist.";
            return false;
        }

        Reset();
        var dialogue = GetYarnDialogue();
        dialogue.SetProgram(_narrative.Yarn.Program);
        dialogue.SetNode(nodeName);
        _yarnActive = true;
        Publish("Dialogue.Started", nodeName);
        PumpYarn();
        return true;
    }

    /// <summary>Start a JSON dialogue-tree conversation.</summary>
    public bool StartTree(string treeId, out string? error)
    {
        error = null;
        if (!_narrative.Session.Defs.TryGet<DialogueTreeDef>(treeId, out var def))
        {
            error = $"Dialogue tree '{treeId}' does not exist.";
            return false;
        }

        Reset();
        _tree = new TreeCursor(def);
        Publish("Dialogue.Started", treeId);
        EnterTreeNode(def.Start);
        return true;
    }

    /// <summary>Continue past the current line.</summary>
    public void Advance()
    {
        if (State != DialogueState.Line)
        {
            return;
        }

        if (_yarnActive)
        {
            PumpYarn();
        }
        else if (_tree is not null)
        {
            AfterTreeLine();
        }
    }

    /// <summary>Select an option by its <see cref="DialogueOption.Id"/>.</summary>
    public void Choose(int optionId)
    {
        if (State != DialogueState.Options)
        {
            return;
        }

        if (_yarnActive)
        {
            GetYarnDialogue().SetSelectedOption(optionId);
            State = DialogueState.Idle;
            PumpYarn();
        }
        else if (_tree is not null)
        {
            ChooseTreeOption(optionId);
        }
    }

    public void Stop()
    {
        if (_yarnActive)
        {
            GetYarnDialogue().Stop();
        }

        Reset();
        State = DialogueState.Idle;
    }

    /// <summary>The conversing player entity (first entity tagged "player").</summary>
    public Entity? Player => _narrative.Session.World.All.FirstOrDefault(e => e.Tags.Contains("player"));

    // ─── Yarn backend ────────────────────────────────────────────────

    private Dialogue GetYarnDialogue()
    {
        if (_yarn is not null)
        {
            return _yarn;
        }

        _yarn = new Dialogue(new Yarn.BlackboardVariableStorage(_narrative.Session.Flags))
        {
            LogDebugMessage = _ => { },
            LogErrorMessage = message => _narrative.Session.Services.Host.Logger.Error($"Yarn: {message}"),
        };
        _yarn.Library.ImportLibrary(_narrative.BuildFunctionLibrary());

        _yarn.LineHandler = line =>
        {
            SetLine(_narrative.Yarn.GetText(line));
            _yarnDelivered = true;
        };
        _yarn.OptionsHandler = optionSet =>
        {
            Options = optionSet.Options
                .Where(o => o.IsAvailable)
                .Select(o => new DialogueOption(o.ID, StripSpeaker(_narrative.Yarn.GetText(o.Line))))
                .ToList();
            State = DialogueState.Options;
            Publish("Dialogue.Options", null);
            _yarnDelivered = true;
        };
        _yarn.CommandHandler = command =>
        {
            _narrative.HandleDialogueCommand(command.Text);
            _yarnCommandHandled = true;
        };
        _yarn.DialogueCompleteHandler = () =>
        {
            State = DialogueState.Ended;
            _yarnActive = false;
            Publish("Dialogue.Ended", null);
            _yarnDelivered = true;
        };
        return _yarn;
    }

    private void PumpYarn()
    {
        var dialogue = GetYarnDialogue();
        _yarnDelivered = false;
        while (!_yarnDelivered)
        {
            _yarnCommandHandled = false;
            dialogue.Continue();
            if (!_yarnDelivered && !_yarnCommandHandled)
            {
                break; // nothing happened; avoid spinning
            }
        }
    }

    // ─── Tree backend ────────────────────────────────────────────────

    private void EnterTreeNode(string nodeId)
    {
        var cursor = _tree!;
        if (!cursor.Def.Nodes.TryGetValue(nodeId, out var node))
        {
            _narrative.Session.Services.Host.Logger.Error($"Dialogue tree '{cursor.Def.Id}' node '{nodeId}' missing.");
            EndTree();
            return;
        }

        cursor.NodeId = nodeId;
        _narrative.Rpg.RunEffects(node.Effects, source: null, target: Player);

        if (node.Line is not null)
        {
            Speaker = node.Speaker;
            Line = node.Line;
            State = DialogueState.Line;
            Publish("Dialogue.Line", node.Line);
        }
        else
        {
            AfterTreeLine();
        }
    }

    private void AfterTreeLine()
    {
        var cursor = _tree!;
        var node = cursor.Def.Nodes[cursor.NodeId];
        var conditionContext = new ConditionContext
        {
            Session = _narrative.Session,
            Rpg = _narrative.Rpg,
            Subject = Player,
        };

        var eligible = (node.Options ?? [])
            .Select((option, index) => (Option: option, Index: index))
            .Where(x => _narrative.Rpg.Conditions.EvaluateAll(x.Option.Conditions, conditionContext))
            .ToList();

        if (eligible.Count > 0)
        {
            cursor.EligibleOptions = eligible;
            Options = eligible.Select(x => new DialogueOption(x.Index, x.Option.Text)).ToList();
            State = DialogueState.Options;
            Publish("Dialogue.Options", null);
        }
        else if (node.Next is not null)
        {
            EnterTreeNode(node.Next);
        }
        else
        {
            EndTree();
        }
    }

    private void ChooseTreeOption(int optionId)
    {
        var cursor = _tree!;
        var match = cursor.EligibleOptions.FirstOrDefault(x => x.Index == optionId);
        if (match.Option is null)
        {
            return;
        }

        _narrative.Rpg.RunEffects(match.Option.Effects, source: null, target: Player);
        if (match.Option.Next is not null)
        {
            EnterTreeNode(match.Option.Next);
        }
        else
        {
            EndTree();
        }
    }

    private void EndTree()
    {
        _tree = null;
        State = DialogueState.Ended;
        Publish("Dialogue.Ended", null);
    }

    // ─── shared ──────────────────────────────────────────────────────

    private void SetLine(string rawText)
    {
        // Yarn convention: "Speaker: line text"
        var separator = rawText.IndexOf(": ", StringComparison.Ordinal);
        if (separator > 0)
        {
            Speaker = rawText[..separator];
            Line = rawText[(separator + 2)..];
        }
        else
        {
            Speaker = null;
            Line = rawText;
        }

        State = DialogueState.Line;
        Publish("Dialogue.Line", Line);
    }

    private static string StripSpeaker(string text)
    {
        var separator = text.IndexOf(": ", StringComparison.Ordinal);
        return separator > 0 ? text[(separator + 2)..] : text;
    }

    private void Reset()
    {
        _tree = null;
        _yarnActive = false;
        Speaker = null;
        Line = null;
        Options = [];
        State = DialogueState.Idle;
    }

    private void Publish(string topic, string? detail)
        => _narrative.Session.Events.Publish(topic, EventPayload.Of(("detail", detail)), _narrative.Session.Tick);

    private sealed class TreeCursor(DialogueTreeDef def)
    {
        public DialogueTreeDef Def { get; } = def;

        public string NodeId { get; set; } = "";

        public List<(DialogueTreeDef.TreeOption Option, int Index)> EligibleOptions { get; set; } = [];
    }
}
