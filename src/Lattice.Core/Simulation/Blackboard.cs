namespace Lattice.Core.Simulation;

/// <summary>
/// Timestamped key/value store with staleness queries and subscriptions —
/// the shared-knowledge pattern from the research (emergent-ai-guide ch04
/// §4.4). One global instance holds world flags; AI groups get scoped
/// instances (M4). Values are constrained to bool / double / string so the
/// store serializes losslessly into saves; numeric types are coerced.
/// </summary>
public sealed class Blackboard
{
    private readonly Dictionary<string, object> _data = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _writeTimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Action<string, object?>>> _subscribers = new(StringComparer.Ordinal);
    private readonly Func<double> _clock;

    /// <param name="clock">Time source for write timestamps — simulation seconds, not wall time.</param>
    public Blackboard(Func<double>? clock = null)
    {
        _clock = clock ?? (static () => 0.0);
    }

    public IEnumerable<string> Keys => _data.Keys;

    public int Count => _data.Count;

    public void Write(string key, object value)
    {
        var stored = Coerce(value);
        _data[key] = stored;
        _writeTimes[key] = _clock();
        if (_subscribers.TryGetValue(key, out var subs))
        {
            // copy: a callback may subscribe/unsubscribe re-entrantly
            foreach (var callback in subs.ToArray())
            {
                callback(key, stored);
            }
        }
    }

    public bool HasKey(string key) => _data.ContainsKey(key);

    public object? Read(string key, object? fallback = null) => _data.TryGetValue(key, out var value) ? value : fallback;

    public bool TryRead(string key, out object value) => _data.TryGetValue(key, out value!);

    public bool ReadBool(string key, bool fallback = false) => _data.TryGetValue(key, out var v) && v is bool b ? b : fallback;

    public double ReadNumber(string key, double fallback = 0) => _data.TryGetValue(key, out var v) && v is double d ? d : fallback;

    public string? ReadString(string key, string? fallback = null) => _data.TryGetValue(key, out var v) && v is string s ? s : fallback;

    public (object? Value, double AgeSeconds) ReadWithAge(string key)
    {
        if (!_data.TryGetValue(key, out var value))
        {
            return (null, double.PositiveInfinity);
        }

        return (value, _clock() - _writeTimes[key]);
    }

    public bool IsStale(string key, double maxAgeSeconds)
    {
        if (!_writeTimes.TryGetValue(key, out var written))
        {
            return true;
        }

        return _clock() - written > maxAgeSeconds;
    }

    public bool Clear(string key)
    {
        _writeTimes.Remove(key);
        return _data.Remove(key);
    }

    public void ClearAll()
    {
        _data.Clear();
        _writeTimes.Clear();
    }

    public IDisposable Subscribe(string key, Action<string, object?> callback)
    {
        if (!_subscribers.TryGetValue(key, out var subs))
        {
            subs = [];
            _subscribers[key] = subs;
        }

        subs.Add(callback);
        return new Subscription(subs, callback);
    }

    /// <summary>Snapshot of all entries for the save system.</summary>
    public IReadOnlyDictionary<string, object> Export() => new Dictionary<string, object>(_data, StringComparer.Ordinal);

    private static object Coerce(object value)
    {
        return value switch
        {
            bool or double or string => value,
            sbyte or byte or short or ushort or int or uint or long or ulong or float or decimal =>
                Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
            null => throw new ArgumentNullException(nameof(value), "Use Clear(key) instead of writing null."),
            _ => throw new ArgumentException(
                $"Blackboard values must be bool, double, or string; got {value.GetType().Name}.", nameof(value)),
        };
    }

    private sealed class Subscription(List<Action<string, object?>> subs, Action<string, object?> callback) : IDisposable
    {
        public void Dispose() => subs.Remove(callback);
    }
}
