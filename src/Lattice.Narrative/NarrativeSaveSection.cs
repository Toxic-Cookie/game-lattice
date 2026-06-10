using System.Text.Json;
using Lattice.Core.Content;
using Lattice.Core.Persistence;
using Lattice.Core.Simulation;

namespace Lattice.Narrative;

/// <summary>
/// The "narrative" slice of the save file: quest statuses and step indices.
/// Counters and dialogue-set variables live on the global blackboard, which
/// the core delta already persists. Active conversations are not saved (v1).
/// </summary>
internal sealed class NarrativeSaveSection(NarrativeRuntime narrative) : ISaveSection
{
    public string Key => "narrative";

    public JsonElement Capture(GameSession session)
    {
        var data = new SectionData();
        foreach (var pair in narrative.Quests.Export().OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            data.Quests[pair.Key] = new SavedQuest
            {
                Status = pair.Value.Status.ToString(),
                StepIndex = pair.Value.StepIndex,
            };
        }

        return JsonSerializer.SerializeToElement(data, ContentLoader.JsonOptions);
    }

    public void Restore(GameSession session, JsonElement data, ContentLoadReport report)
    {
        SectionData? section;
        try
        {
            section = data.Deserialize<SectionData>(ContentLoader.JsonOptions);
        }
        catch (JsonException ex)
        {
            report.Errors.Add($"narrative save section is invalid: {ex.Message}");
            return;
        }

        narrative.Quests.ClearStates();
        foreach (var pair in section?.Quests ?? [])
        {
            if (!Enum.TryParse<QuestStatus>(pair.Value.Status, out var status))
            {
                report.Warnings.Add($"Quest '{pair.Key}' has unknown saved status '{pair.Value.Status}'; dropped.");
                continue;
            }

            narrative.Quests.RestoreState(pair.Key, status, pair.Value.StepIndex);
        }
    }

    private sealed class SectionData
    {
        public Dictionary<string, SavedQuest> Quests { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class SavedQuest
    {
        public string Status { get; set; } = "";

        public int StepIndex { get; set; }
    }
}
