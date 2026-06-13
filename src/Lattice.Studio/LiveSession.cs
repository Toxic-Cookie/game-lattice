using System.Diagnostics;
using Lattice.Core.Content;
using Lattice.Core.Events;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using LogLevel = Lattice.Core.Hosting.LogLevel;

namespace Lattice.Studio;

/// <summary>
/// A persistent, engine-equivalent content session: it watches the content
/// directory and applies file changes through the very same
/// <see cref="HotReloadManager"/> a running Godot/Unity host uses — debounce,
/// per-file reload, broken-edit rejection (old defs kept), and the post-reload
/// link pass. So whatever the editor (or a hand edit, or an LLM) writes, this
/// mirror shows exactly what a live engine would see: defs reloaded, or an
/// edit rejected. Surfaced in the UI as the "live" indicator (plan/08 M8.5).
/// </summary>
public sealed class LiveSession : IDisposable
{
    private readonly object _gate = new();
    private readonly DirectoryContentSource _source;
    private readonly DefRegistry _registry = new();
    private readonly EventBus _events;
    private readonly HotReloadManager _hot;
    private readonly NCalcFormulaEngine _formulas = new(new LatticeRandom(0));
    private readonly CapturingLogger _logger = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Timer _pump;

    private int _reloads;
    private DateTimeOffset? _lastReloadUtc;
    private string[] _lastReloaded = [];
    private bool _healthy = true;

    public LiveSession(string contentDir, DefTypeRegistry types)
    {
        _source = new DirectoryContentSource(contentDir, watch: true);
        _events = new EventBus(_logger);
        var loader = new ContentLoader(types);
        loader.LoadAll(_source, _registry);

        _hot = new HotReloadManager(_source, loader, _registry, _events, _logger, () => _clock.Elapsed.TotalSeconds);
        _events.Subscribe("Content.Reloaded", evt =>
        {
            _reloads++;
            _lastReloadUtc = DateTimeOffset.UtcNow;
            _lastReloaded = evt.Payload.TryGetValue("defIds", out var v) && v is string ids
                ? ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                : [];
        });

        // Mirror the engine's tick: drain the watcher through the reload manager.
        _pump = new Timer(_ => Tick(), null, 250, 250);
    }

    public object Status()
    {
        lock (_gate)
        {
            return new
            {
                defs = _registry.Count,
                reloads = _reloads,
                lastReloadUtc = _lastReloadUtc,
                lastReloaded = _lastReloaded,
                healthy = _healthy,
                log = _logger.Recent(),
            };
        }
    }

    public void Dispose()
    {
        _pump.Dispose();
        _hot.Dispose();
        _source.Dispose();
    }

    private void Tick()
    {
        lock (_gate)
        {
            try
            {
                var errorsBefore = _logger.ErrorCount;
                var processed = _hot.Pump(_formulas);
                _events.DispatchPending();
                if (processed > 0)
                {
                    // A cycle that did work is healthy iff it logged no new errors —
                    // so a rejected edit goes red, and fixing or deleting it recovers.
                    _healthy = _logger.ErrorCount == errorsBefore;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"live pump error: {ex.Message}");
            }
        }
    }

    /// <summary>Keeps a small ring of recent log lines and a running count of errors.</summary>
    private sealed class CapturingLogger : ILatticeLogger
    {
        private readonly object _l = new();
        private readonly Queue<(DateTimeOffset Time, LogLevel Level, string Message)> _buf = new();

        public int ErrorCount { get; private set; }

        public void Log(LogLevel level, string message)
        {
            lock (_l)
            {
                if (level == LogLevel.Error)
                {
                    ErrorCount++;
                }

                _buf.Enqueue((DateTimeOffset.UtcNow, level, message));
                while (_buf.Count > 40)
                {
                    _buf.Dequeue();
                }
            }
        }

        public IReadOnlyList<object> Recent()
        {
            lock (_l)
            {
                return _buf.Reverse().Take(12)
                    .Select(e => (object)new { time = e.Time, level = e.Level.ToString(), message = e.Message })
                    .ToList();
            }
        }
    }
}
