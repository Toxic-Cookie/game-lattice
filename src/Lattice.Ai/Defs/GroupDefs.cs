using Lattice.Core.Content;

namespace Lattice.Ai.Defs;

/// <summary>
/// A role members of a group can fill (ch04 §4.6, HZD Part 2). Slot limits
/// are the whole mechanism: the herd's core/ring structure is emergent from
/// "2 watchers, everyone else grazes" — no formation code.
/// </summary>
public sealed class RoleDef : Def
{
    /// <summary>How many members may hold this role at once.</summary>
    public int Slots { get; set; } = int.MaxValue;

    /// <summary>Manual condition set on assignees (must exist in their catalog), e.g. "ROLE_WATCHER".</summary>
    public string? Condition { get; set; }

    /// <summary>
    /// When set, assignees get a "post_position" belief on a ring of this
    /// radius around the group centroid, evenly spaced by slot index —
    /// structure from slots, not scripts.
    /// </summary>
    public double? RingRadius { get; set; }
}

/// <summary>
/// A group archetype (HZD Part 2): which roles exist (in fill-priority
/// order), how stale shared knowledge may get, and how alert decays. The
/// runtime group agent is non-physical — a member list, a scoped
/// blackboard, an alert level, and role assignments.
/// </summary>
public sealed class GroupDef : Def
{
    /// <summary>Role def IDs in fill-priority order (first roles fill first).</summary>
    [LatticeRef("role")]
    public List<string> Roles { get; set; } = [];

    /// <summary>Per-key staleness thresholds in seconds for member reads (latency is a feature — HZD Part 5).</summary>
    public Dictionary<string, double>? Staleness { get; set; }

    /// <summary>Seconds without fresh threat knowledge before the alert level steps down one stage.</summary>
    public double AlertDecaySeconds { get; set; } = 8.0;

    /// <summary>Member capacity (recycling refuses full groups).</summary>
    public int MaxMembers { get; set; } = 32;

    /// <summary>Below this, surviving members become candidates for passport recycling.</summary>
    public int MinMembers { get; set; } = 2;

    /// <summary>Passports this group accepts when the collective recycles strays.</summary>
    public List<string>? Passports { get; set; }

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var role in Roles)
        {
            yield return new DefReference(role, $"{Id}.roles");
        }
    }
}

/// <summary>
/// The Collective (ch04 §4.5, HZD Part 4): spawn sites that assemble groups,
/// a global AI budget, and passport recycling for stranded agents.
/// </summary>
public sealed class CollectiveDef : Def
{
    /// <summary>Maximum live agents this collective tolerates; the farthest-from-player are despawned first.</summary>
    public int Budget { get; set; } = 64;

    public List<Site> Sites { get; set; } = [];

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var site in Sites)
        {
            yield return new DefReference(site.Group, $"{Id}.sites");
            foreach (var member in site.Members)
            {
                yield return new DefReference(member.Entity, $"{Id}.sites");
            }
        }
    }

    public sealed class Site
    {
        public float[] Position { get; set; } = [0, 0, 0];

        /// <summary>Group def ID instantiated at this site.</summary>
        [LatticeRef("group")]
        public string Group { get; set; } = "";

        public List<Member> Members { get; set; } = [];

        /// <summary>Members spawn scattered within this radius of the site.</summary>
        public double SpawnRadius { get; set; } = 3.0;
    }

    public sealed class Member
    {
        [LatticeRef("entity")]
        public string Entity { get; set; } = "";

        public int Count { get; set; } = 1;
    }
}

/// <summary>
/// A declarative detector over player-behavior events (concept Phase 4):
/// when <see cref="Watch"/> fires <see cref="Threshold"/> times within
/// <see cref="Window"/> seconds for an agent, <see cref="SetCondition"/> is
/// set on that agent — and existing brains react through their normal
/// machinery. No special code path.
/// </summary>
public sealed class MetaSensorDef : Def
{
    /// <summary>Event topic to watch (e.g. "Player.Poked", "Dialogue.PlayerLookedAway").</summary>
    public string Watch { get; set; } = "";

    /// <summary>Sliding window in seconds.</summary>
    public double Window { get; set; } = 5.0;

    /// <summary>Occurrences within the window required to trip.</summary>
    public int Threshold { get; set; } = 1;

    /// <summary>Condition name set on the agent while tripped (cleared when the window drains).</summary>
    public string SetCondition { get; set; } = "";

    /// <summary>Payload key naming the affected agent's instance ID.</summary>
    public string AgentKey { get; set; } = "agentId";
}
