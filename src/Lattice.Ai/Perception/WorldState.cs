using System.Numerics;

namespace Lattice.Ai.Perception;

/// <summary>
/// An agent's rich belief store: string-keyed facts populated only by
/// sensors and (M4d) blackboard sync — never by peeking at ground truth
/// (research ch07 "too much information" anti-pattern). Copy/apply support
/// exists for the M4c/M4d planners, which simulate over belief snapshots.
/// </summary>
public sealed class WorldState
{
    private readonly Dictionary<string, object> _facts;

    public WorldState()
    {
        _facts = new Dictionary<string, object>(StringComparer.Ordinal);
    }

    private WorldState(Dictionary<string, object> facts)
    {
        _facts = new Dictionary<string, object>(facts, StringComparer.Ordinal);
    }

    public int Count => _facts.Count;

    public IEnumerable<KeyValuePair<string, object>> Facts => _facts;

    public void Set(string key, object value) => _facts[key] = value;

    public bool Remove(string key) => _facts.Remove(key);

    public void Clear() => _facts.Clear();

    public bool Has(string key) => _facts.ContainsKey(key);

    public object? Get(string key) => _facts.TryGetValue(key, out var value) ? value : null;

    public bool GetBool(string key, bool fallback = false)
        => _facts.TryGetValue(key, out var v) && v is bool b ? b : fallback;

    public double GetNumber(string key, double fallback = 0)
        => _facts.TryGetValue(key, out var v) && v is double d ? d : fallback;

    public string? GetString(string key, string? fallback = null)
        => _facts.TryGetValue(key, out var v) && v is string s ? s : fallback;

    public Vector3? GetPosition(string key)
        => _facts.TryGetValue(key, out var v) && v is Vector3 p ? p : null;

    public WorldState Copy() => new(_facts);
}
