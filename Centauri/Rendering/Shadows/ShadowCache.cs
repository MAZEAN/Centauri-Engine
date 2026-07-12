namespace Centauri.Rendering.Shadows;

using System.Numerics;

using Utils.Misc;

// Tracks the fitted cascades the current depth maps were actually rendered for. Comparing the
// FITTED result (post texel/radius/Z snapping — see CascadeBuilder) rather than the raw camera
// view*projection matrix matters: CascadeBuilder's radius/Z snapping already collapses many
// nearby camera positions onto the exact same fitted cascade, but the raw camera matrix changes
// on every single frame the camera moves at all, however slightly. Keying on the raw matrix
// threw away those free cache hits — literally every frame of camera motion forced a full
// cascade redraw even when the fitted result (and therefore the correct rendered depth) would
// have been bit-identical to last frame's.
//
// The maps may be reused when the incoming cascades match, and either nothing is animating the
// casters this frame, or it is but we're still inside the animating-caster throttle window (see
// CanReuse) — wind sway is slow and PCSS already softens edges, so redrawing the full cascade
// set every single frame just to track a few pixels of foliage motion is wasted GPU time;
// lagging a fraction of a second behind the animation is imperceptible. Throttling is time- not
// frame-based so the same setting gives the same real-world lag regardless of framerate.
internal sealed class ShadowCache
{
    private const float Tolerance = 0.001f;
    
    private Vector3   _sunDir;
    private int       _revision;
    private Cascade[] _cascades = [];
    private bool      _valid;
    private float     _lastRenderTime;

    // `cascades` is the caller's live, in-place-mutated array (see CascadeBuilder.Build) — only
    // ever read here, never stored directly (see Record).
    public bool CanReuse(Vector3 sunDir, int revision, Cascade[] cascades, bool castersAnimating, float animatingThrottleSeconds)
    {
        if (!_valid || sunDir != _sunDir || revision != _revision || !CascadesEqual(cascades, _cascades))
            return false;
        if (!castersAnimating)
            return true;   // static scene — nothing to catch up on regardless
        if (animatingThrottleSeconds <= 0f || Time.Now - _lastRenderTime >= animatingThrottleSeconds)
            return false;   // redraw now to catch up with the animation
        return true;   // reuse a still-recent render while only wind is moving
    }

    // Snapshots `cascades` (Clone — the caller's array is mutated in place next frame, so a
    // plain reference would silently "update" this record to next frame's values too).
    public void Record(Vector3 sunDir, int revision, Cascade[] cascades)
    {
        _sunDir         = sunDir;
        _revision       = revision;
        _cascades       = (Cascade[])cascades.Clone();
        _valid          = true;
        _lastRenderTime = Time.Now;
    }

    public void Invalidate() => _valid = false;

    // Matrix fully determines what gets rendered/sampled; SplitDepth is compared too since it
    // drives cascade selection in the lit shader and is otherwise free to check.
    private static bool CascadesEqual(Cascade[] a, Cascade[] b)
    {
        if (a.Length != b.Length) return false;

        for (var i = 0; i < a.Length; i++)
            if (a[i].Matrix != b[i].Matrix || Math.Abs(a[i].SplitDepth - b[i].SplitDepth) > Tolerance)
                return false;

        return true;
    }
}
