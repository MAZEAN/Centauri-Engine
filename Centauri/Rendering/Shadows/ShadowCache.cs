namespace Centauri.Rendering.Shadows;

using System.Numerics;

using Utils.Misc;

// Everything that shapes the cascade depth maps. Two equal keys mean last frame's maps are
// still valid, so the whole shadow render can be skipped. Value equality over the sun
// direction, the camera (cascades fit its frustum), the scene revision and the cascade-fit
// config — the sampling-side params (bias, pcf) don't change the stored depth, so they're out.
internal readonly struct ShadowCacheKey : IEquatable<ShadowCacheKey>
{
    private const float Tolerance = 0.01f;
    
    private readonly Vector3   _sunDir;
    private readonly Matrix4x4 _viewProj;
    private readonly int       _revision;
    private readonly int       _cascadeCount;
    private readonly float     _distance;
    private readonly float     _splitLambda;

    public ShadowCacheKey(Vector3 sunDir, Matrix4x4 viewProj, int revision,
                          int cascadeCount, float distance, float splitLambda)
    {
        _sunDir       = sunDir;
        _viewProj     = viewProj;
        _revision     = revision;
        _cascadeCount = cascadeCount;
        _distance     = distance;
        _splitLambda  = splitLambda;
    }

    public bool Equals(ShadowCacheKey o) =>
        _sunDir       == o._sunDir       &&
        _viewProj     == o._viewProj     &&
        _revision     == o._revision     &&
        _cascadeCount == o._cascadeCount &&
        Math.Abs(_distance - o._distance) < Tolerance &&
        Math.Abs(_splitLambda - o._splitLambda) < Tolerance;

    public override bool Equals(object? o) => o is ShadowCacheKey k && Equals(k);
    public override int GetHashCode() =>
        HashCode.Combine(_sunDir, _viewProj, _revision, _cascadeCount, _distance, _splitLambda);
}

// Tracks the key the current depth maps were rendered for. The maps may be reused when the
// incoming key matches, and either nothing is animating the casters this frame, or it is but
// we're still inside the animating-caster throttle window (see CanReuse) — wind sway is slow
// and PCSS already softens edges, so redrawing the full cascade set every single frame just to
// track a few pixels of foliage motion is wasted GPU time; lagging a fraction of a second behind
// the animation is imperceptible. Throttling is time- rather than frame-based so the same
// setting gives the same real-world lag regardless of framerate.
internal sealed class ShadowCache
{
    private ShadowCacheKey _key;
    private bool           _valid;
    private float          _lastRenderTime;

    public bool CanReuse(in ShadowCacheKey key, bool castersAnimating, float animatingThrottleSeconds)
    {
        if (!_valid || !key.Equals(_key))
            return false;
        if (!castersAnimating)
            return true;   // static scene — nothing to catch up on regardless
        if (animatingThrottleSeconds <= 0f || Time.Now - _lastRenderTime >= animatingThrottleSeconds)
            return false;   // redraw now to catch up with the animation
        return true;   // reuse a still-recent render while only wind is moving
    }

    public void Record(in ShadowCacheKey key)
    {
        _key            = key;
        _valid          = true;
        _lastRenderTime = Time.Now;
    }

    public void Invalidate() => _valid = false;
}
