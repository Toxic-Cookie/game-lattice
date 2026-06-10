using System.Numerics;
using System.Text.Json;
using Lattice.Core.Content;
using Lattice.Core.Simulation;

namespace Lattice.Core.Persistence;

/// <summary>
/// A module-owned slice of the save file. Modules register sections on the
/// session; capture runs after the core delta, restore runs after entities
/// exist again (so sections can re-derive component state).
/// </summary>
public interface ISaveSection
{
    /// <summary>Stable key under <see cref="SaveGame.Sections"/>.</summary>
    string Key { get; }

    JsonElement Capture(GameSession session);

    void Restore(GameSession session, JsonElement data, ContentLoadReport report);
}

/// <summary>Serialized world delta: only what diverged from base content (plan/01 §5). Defs are never saved.</summary>
public sealed class SaveGame
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public long Tick { get; set; }

    public double SimTimeSeconds { get; set; }

    public ulong RngState { get; set; }

    public long NextEntityOrdinal { get; set; }

    public Dictionary<string, JsonElement> Flags { get; set; } = [];

    public List<SavedEntity> Entities { get; set; } = [];

    /// <summary>Module sections keyed by <see cref="ISaveSection.Key"/> (e.g. "rpg").</summary>
    public Dictionary<string, JsonElement> Sections { get; set; } = [];

    public sealed class SavedEntity
    {
        public string InstanceId { get; set; } = "";

        public string DefId { get; set; } = "";

        public string? Name { get; set; }

        public List<string> Tags { get; set; } = [];

        public Dictionary<string, double> Stats { get; set; } = [];

        public float[] Position { get; set; } = [0, 0, 0];
    }
}

/// <summary>
/// Captures and restores the world delta. AI/plan state is deliberately not
/// saved — brains replan from world-observable facts on load (plan/01 §5).
/// Blackboard write-ages are not preserved in v1; restored keys read as
/// freshly written.
/// </summary>
public static class SaveManager
{
    public static string Capture(GameSession session)
    {
        var save = new SaveGame
        {
            Tick = session.Tick,
            SimTimeSeconds = session.SimTimeSeconds,
            RngState = session.Rng.State,
            NextEntityOrdinal = session.World.NextEntityOrdinal,
        };

        foreach (var pair in session.Flags.Export())
        {
            save.Flags[pair.Key] = JsonSerializer.SerializeToElement(pair.Value, ContentLoader.JsonOptions);
        }

        foreach (var entity in session.World.All.OrderBy(e => e.InstanceId, StringComparer.Ordinal))
        {
            save.Entities.Add(new SaveGame.SavedEntity
            {
                InstanceId = entity.InstanceId,
                DefId = entity.DefId,
                Name = entity.Name,
                Tags = entity.Tags.OrderBy(t => t, StringComparer.Ordinal).ToList(),
                Stats = new Dictionary<string, double>(entity.Stats, StringComparer.Ordinal),
                Position = [entity.Position.X, entity.Position.Y, entity.Position.Z],
            });
        }

        foreach (var section in session.SaveSections)
        {
            save.Sections[section.Key] = section.Capture(session);
        }

        return JsonSerializer.Serialize(save, ContentLoader.JsonOptions);
    }

    /// <summary>Restore a session from saved JSON. Returns a report; on errors the session may be partially restored.</summary>
    public static ContentLoadReport Restore(GameSession session, string saveJson)
    {
        var report = new ContentLoadReport();

        SaveGame? save;
        try
        {
            save = JsonSerializer.Deserialize<SaveGame>(saveJson, ContentLoader.JsonOptions);
        }
        catch (JsonException ex)
        {
            report.Errors.Add($"Save file is not valid JSON: {ex.Message}");
            return report;
        }

        if (save is null)
        {
            report.Errors.Add("Save file deserialized to nothing.");
            return report;
        }

        if (save.Version != SaveGame.CurrentVersion)
        {
            // per-system migration hooks land when version 2 exists; v1 just refuses unknown futures
            report.Errors.Add($"Save version {save.Version} is not supported (current: {SaveGame.CurrentVersion}).");
            return report;
        }

        session.Tick = save.Tick;
        session.SimTimeSeconds = save.SimTimeSeconds;
        session.Rng.State = save.RngState;

        session.Flags.ClearAll();
        foreach (var pair in save.Flags)
        {
            if (JsonValueHelper.TryToPlain(pair.Value, out var value))
            {
                session.Flags.Write(pair.Key, value);
            }
            else
            {
                report.Warnings.Add($"Flag '{pair.Key}' has a non-scalar saved value; skipped.");
            }
        }

        session.World.Clear();
        session.World.NextEntityOrdinal = save.NextEntityOrdinal;
        foreach (var saved in save.Entities)
        {
            if (!session.Defs.Contains(saved.DefId))
            {
                report.Errors.Add($"Saved entity {saved.InstanceId} references missing def '{saved.DefId}'.");
                continue;
            }

            var entity = new Entity(saved.InstanceId, saved.DefId)
            {
                Name = saved.Name,
                Position = saved.Position is { Length: 3 } p ? new Vector3(p[0], p[1], p[2]) : default,
            };
            foreach (var tag in saved.Tags)
            {
                entity.Tags.Add(tag);
            }

            foreach (var stat in saved.Stats)
            {
                entity.Stats[stat.Key] = stat.Value;
            }

            session.World.RestoreEntity(entity);
        }

        // module sections last: entities exist, so sections can rebuild component state
        foreach (var section in session.SaveSections)
        {
            if (save.Sections.TryGetValue(section.Key, out var data))
            {
                section.Restore(session, data, report);
            }
        }

        return report;
    }
}
