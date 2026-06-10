using System.Collections.Concurrent;
using Lattice.Core.Events;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting;

namespace Lattice.Core.Content;

/// <summary>
/// Applies content-file changes to the def registry at tick boundaries.
/// Watcher events arrive on arbitrary threads and in bursts (editors write
/// temp files, then rename); they are queued here and only processed on the
/// simulation thread via <see cref="Pump"/>, after a quiet-period debounce.
/// A file that fails to parse keeps its old defs — a broken edit must never
/// kill a live session (plan/01 §6).
/// </summary>
public sealed class HotReloadManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ContentChange> _dirty = new(StringComparer.Ordinal);
    private readonly IContentSource _source;
    private readonly ContentLoader _loader;
    private readonly DefRegistry _registry;
    private readonly EventBus _events;
    private readonly ILatticeLogger _logger;
    private readonly Func<double> _clock;
    private readonly double _debounceSeconds;
    private double _lastChangeAt;

    public HotReloadManager(
        IContentSource source,
        ContentLoader loader,
        DefRegistry registry,
        EventBus events,
        ILatticeLogger logger,
        Func<double> clock,
        double debounceSeconds = 0.25)
    {
        _source = source;
        _loader = loader;
        _registry = registry;
        _events = events;
        _logger = logger;
        _clock = clock;
        _debounceSeconds = debounceSeconds;
        _source.Changed += OnChanged;
    }

    public bool HasPendingChanges => !_dirty.IsEmpty;

    /// <summary>
    /// Process pending changes if the debounce window has elapsed. Call from
    /// the simulation thread (the session does, every tick). Returns the
    /// number of files processed.
    /// </summary>
    public int Pump(IFormulaEngine? formulas = null)
    {
        if (_dirty.IsEmpty || _clock() - _lastChangeAt < _debounceSeconds)
        {
            return 0;
        }

        var processed = 0;
        var reloadedDefIds = new List<string>();

        foreach (var path in _dirty.Keys.ToArray())
        {
            if (!_dirty.TryRemove(path, out var change))
            {
                continue;
            }

            processed++;

            if (change.Kind == ContentChangeKind.Deleted)
            {
                var removed = _registry.RemoveBySourceFile(change.File.RelativePath);
                if (removed.Count > 0)
                {
                    _logger.Info($"Hot reload: {change.File.RelativePath} deleted; removed {removed.Count} def(s).");
                    reloadedDefIds.AddRange(removed);
                }

                continue;
            }

            string text;
            try
            {
                text = _source.ReadAllText(change.File);
            }
            catch (IOException ex)
            {
                _logger.Warning($"Hot reload: cannot read {change.File.RelativePath} ({ex.Message}); keeping old defs.");
                continue;
            }

            var report = _loader.LoadFile(change.File, text, _registry, replace: true);
            if (!report.Ok)
            {
                foreach (var error in report.Errors)
                {
                    _logger.Error($"Hot reload rejected: {error}");
                }

                _logger.Warning($"Hot reload: {change.File.RelativePath} kept its previous defs.");
                continue;
            }

            _logger.Info($"Hot reload: {change.File.RelativePath} applied ({report.DefsLoaded} def(s)).");
            reloadedDefIds.AddRange(
                _registry.AllDefs.Where(d => d.SourceFile == change.File.RelativePath).Select(d => d.Id));
        }

        if (reloadedDefIds.Count > 0)
        {
            // post-reload link pass: report (but don't revert) dangling refs
            var linkReport = new ContentLoadReport();
            _registry.Validate(linkReport, formulas);
            foreach (var error in linkReport.Errors)
            {
                _logger.Warning($"Post-reload validation: {error}");
            }

            _events.Publish("Content.Reloaded", EventPayload.Of(("defIds", string.Join(",", reloadedDefIds))));
        }

        return processed;
    }

    public void Dispose() => _source.Changed -= OnChanged;

    private void OnChanged(ContentChange change)
    {
        if (!change.File.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _dirty[change.File.RelativePath] = change;
        _lastChangeAt = _clock();
    }
}
