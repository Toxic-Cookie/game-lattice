namespace Lattice.Core.Hosting.Standalone;

/// <summary>
/// Headless animation stub: every animation "plays" for a fixed duration of
/// simulation time, advanced by the host calling <see cref="Advance"/> each
/// tick. Gives the AI layer honest play/complete/interruptible semantics
/// without a renderer.
/// </summary>
public sealed class TimedStubAnimationService : IAnimationService
{
    private readonly record struct ActiveAnimation(string AnimationId, bool Interruptible, double EndsAt);

    private readonly Dictionary<string, ActiveAnimation> _entities = new(StringComparer.Ordinal);
    private readonly double _duration;
    private double _now;

    public TimedStubAnimationService(double animationDurationSeconds = 0.5)
    {
        _duration = animationDurationSeconds;
    }

    /// <summary>Advance the stub's simulation clock; call once per tick.</summary>
    public void Advance(double deltaSeconds) => _now += deltaSeconds;

    public void Play(string entityId, string animationId, bool interruptible = true)
    {
        _entities[entityId] = new ActiveAnimation(animationId, interruptible, _now + _duration);
    }

    public void Stop(string entityId) => _entities.Remove(entityId);

    public bool IsPlaying(string entityId, string animationId)
    {
        return _entities.TryGetValue(entityId, out var anim)
            && anim.AnimationId == animationId
            && _now < anim.EndsAt;
    }

    public bool IsComplete(string entityId, string animationId)
    {
        return _entities.TryGetValue(entityId, out var anim)
            && anim.AnimationId == animationId
            && _now >= anim.EndsAt;
    }

    public bool IsPlayingNonInterruptible(string entityId)
    {
        return _entities.TryGetValue(entityId, out var anim)
            && !anim.Interruptible
            && _now < anim.EndsAt;
    }
}
