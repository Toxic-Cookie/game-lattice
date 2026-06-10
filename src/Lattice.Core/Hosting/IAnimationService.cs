namespace Lattice.Core.Hosting;

/// <summary>
/// Animation seam. The framework decides *which* animation plays *when*
/// (the F.E.A.R. insight: all behavior is "the right animation at the right
/// time"); the host renders it. Interruptibility matters to the AI layer:
/// replanning is blocked while a non-interruptible animation runs
/// (research: ch05 §5.6, F.E.A.R. Part 6).
/// </summary>
public interface IAnimationService
{
    /// <summary>Start an animation on an entity, replacing whatever was playing.</summary>
    void Play(string entityId, string animationId, bool interruptible = true);

    /// <summary>Stop whatever the entity is playing.</summary>
    void Stop(string entityId);

    /// <summary>Is this specific animation currently playing on the entity?</summary>
    bool IsPlaying(string entityId, string animationId);

    /// <summary>Has this animation finished since it was last played? False if never played or still running.</summary>
    bool IsComplete(string entityId, string animationId);

    /// <summary>Is the entity committed to a non-interruptible animation right now?</summary>
    bool IsPlayingNonInterruptible(string entityId);
}
