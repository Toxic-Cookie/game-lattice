using Lattice.Core.Hosting;

namespace Lattice.Core.Events;

/// <summary>A published event: string topic + string-keyed payload, so JSON-defined triggers can match without typed contracts.</summary>
public sealed class GameEvent
{
    public required string Topic { get; init; }

    public long Tick { get; init; }

    public IReadOnlyDictionary<string, object?> Payload { get; init; } = EventPayload.Empty;
}

/// <summary>Helpers for building event payloads tersely.</summary>
public static class EventPayload
{
    public static readonly IReadOnlyDictionary<string, object?> Empty =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, object?> Of(params (string Key, object? Value)[] pairs)
    {
        var dict = new Dictionary<string, object?>(pairs.Length, StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            dict[key] = value;
        }

        return dict;
    }
}

/// <summary>
/// String-topic pub/sub bus — the spine connecting quests, schedules, world
/// triggers, and meta-awareness (plan/01 §2). Publishing enqueues; events are
/// delivered when the owner drains the queue at a fixed point in the tick, so
/// no handler ever runs re-entrantly mid-system. Topic subscriptions ending
/// in '*' are prefix matches ("Player*"). Scoped buses forward every local
/// publish to their parent's queue (scene bus bubbles to global).
/// </summary>
public sealed class EventBus
{
    private readonly List<(string Pattern, Action<GameEvent> Handler)> _subscriptions = [];
    private readonly Queue<GameEvent> _pending = new();
    private readonly GameEvent[] _trace;
    private readonly EventBus? _parent;
    private readonly ILatticeLogger? _logger;
    private int _traceNext;
    private int _traceCount;

    public EventBus(ILatticeLogger? logger = null, int traceCapacity = 256)
        : this(logger, null, traceCapacity)
    {
    }

    private EventBus(ILatticeLogger? logger, EventBus? parent, int traceCapacity)
    {
        _logger = logger;
        _parent = parent;
        _trace = new GameEvent[Math.Max(1, traceCapacity)];
    }

    public int PendingCount => _pending.Count;

    /// <summary>Create a child bus whose publishes also bubble to this bus.</summary>
    public EventBus CreateScope() => new(_logger, this, _trace.Length);

    /// <summary>Subscribe to a topic, or a topic prefix when <paramref name="topicPattern"/> ends with '*'.</summary>
    public IDisposable Subscribe(string topicPattern, Action<GameEvent> handler)
    {
        var entry = (topicPattern, handler);
        _subscriptions.Add(entry);
        return new Subscription(this, entry);
    }

    /// <summary>Enqueue an event for delivery at the next dispatch point.</summary>
    public void Publish(string topic, IReadOnlyDictionary<string, object?>? payload = null, long tick = 0)
    {
        var evt = new GameEvent { Topic = topic, Payload = payload ?? EventPayload.Empty, Tick = tick };
        Enqueue(evt);
        _parent?.Enqueue(evt);
    }

    /// <summary>
    /// Deliver all pending events (including ones published by handlers during
    /// this drain), up to a cascade cap that turns runaway feedback loops into
    /// a logged error instead of a hang.
    /// </summary>
    public int DispatchPending(int cascadeLimit = 10_000)
    {
        var delivered = 0;
        while (_pending.Count > 0)
        {
            if (delivered >= cascadeLimit)
            {
                _logger?.Error($"EventBus cascade limit ({cascadeLimit}) hit; {_pending.Count} event(s) dropped.");
                _pending.Clear();
                break;
            }

            var evt = _pending.Dequeue();
            delivered++;
            Record(evt);

            // snapshot: handlers may subscribe/unsubscribe during delivery
            foreach (var (pattern, handler) in _subscriptions.ToArray())
            {
                if (!Matches(pattern, evt.Topic))
                {
                    continue;
                }

                try
                {
                    handler(evt);
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Event handler for '{pattern}' threw on '{evt.Topic}': {ex.Message}");
                }
            }
        }

        return delivered;
    }

    /// <summary>The most recent dispatched events, oldest first (debug ring buffer).</summary>
    public IReadOnlyList<GameEvent> Trace
    {
        get
        {
            var result = new List<GameEvent>(_traceCount);
            for (var i = 0; i < _traceCount; i++)
            {
                result.Add(_trace[(_traceNext - _traceCount + i + _trace.Length * 2) % _trace.Length]);
            }

            return result;
        }
    }

    private static bool Matches(string pattern, string topic)
    {
        if (pattern.Length > 0 && pattern[^1] == '*')
        {
            return topic.AsSpan().StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.Ordinal);
        }

        return string.Equals(pattern, topic, StringComparison.Ordinal);
    }

    private void Enqueue(GameEvent evt) => _pending.Enqueue(evt);

    private void Record(GameEvent evt)
    {
        _trace[_traceNext] = evt;
        _traceNext = (_traceNext + 1) % _trace.Length;
        _traceCount = Math.Min(_traceCount + 1, _trace.Length);
    }

    private sealed class Subscription(EventBus bus, (string, Action<GameEvent>) entry) : IDisposable
    {
        public void Dispose() => bus._subscriptions.Remove(entry);
    }
}
