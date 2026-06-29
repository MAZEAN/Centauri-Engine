namespace Centauri.Rendering.Shadows;

using System.Numerics;

// Everything that shapes the cascade depth maps. Two equal keys mean last frame's maps are
// still valid, so the whole shadow render can be skipped. Value equality over the sun
// direction, the camera (cascades fit its frustum), the scene revision and the cascade-fit
// config — the sampling-side params (bias, pcf) don't change the stored depth, so they're out.
internal readonly struct ShadowCacheKey : IEquatable<ShadowCacheKey>
{
    private const float Tolerance = 0.001f;
    
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

// Tracks the key the current depth maps were rendered for. The maps may be reused only when
// the incoming key matches AND nothing is animating the casters this frame (wind moves
// foliage every frame regardless of the static key), so that gate is passed in per-frame.
internal sealed class ShadowCache
{
    private ShadowCacheKey _key;
    private bool           _valid;

    public bool CanReuse(in ShadowCacheKey key, bool castersAnimating) =>
        _valid && !castersAnimating && key.Equals(_key);

    public void Record(in ShadowCacheKey key)
    {
        _key   = key;
        _valid = true;
    }

    public void Invalidate() => _valid = false;
}
