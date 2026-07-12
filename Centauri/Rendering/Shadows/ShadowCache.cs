namespace Centauri.Rendering.Shadows;

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
// The same reasoning applies to the sun direction, not just the camera — up to a point.
// CascadeBuilder's `view` is built directly from it, so a slow-rotating sun (e.g. a day/night
// cycle) feeds CascadeBuilder a slightly different direction every single frame. But unlike
// camera translation, which slides content within a FIXED light-space frame (exactly what the
// texel/Z snap grid absorbs), rotating the sun rotates that frame's own axes — every point's
// light-space projection shifts non-uniformly, so there's no fixed grid for it to snap onto, and
// the fitted cascades genuinely differ every single frame the light rotates at all. CanReuse
// (the exact path) intentionally does NOT take or compare the raw sun direction — an earlier
// version compared it exactly, which was redundant with, and no looser than, CascadesEqual and
// so caught nothing extra. The actual fix for light-driven churn is CanReuseStaleFit below.
//
// The maps may be exactly reused when the incoming cascades match, and either nothing is
// animating the casters this frame, or it is but we're still inside the animating-caster
// throttle window (see CanReuse) — wind sway is slow and PCSS already softens edges, so
// redrawing the full cascade set every single frame just to track a few pixels of foliage motion
// is wasted GPU time; lagging a fraction of a second behind the animation is imperceptible.
// Throttling is time- not frame-based so the same setting gives the same real-world lag
// regardless of framerate.
internal sealed class ShadowCache
{
    private const float Tolerance = 0.001f;

    private int       _revision;
    private Cascade[] _cascades = [];
    private bool      _valid;
    private float     _lastRenderTime;

    // Exposed so ShadowMapper can substitute these for a fresh (but not actually rendered) fit
    // when CanReuseStaleFit allows reusing an out-of-date light direction — see there for why
    // that substitution is required, not optional.
    public Cascade[] CachedCascades => _cascades;

    // `cascades` is the caller's live, in-place-mutated array (see CascadeBuilder.Build) — only
    // ever read here, never stored directly (see Record).
    public bool CanReuse(int revision, Cascade[] cascades, bool castersAnimating, float animatingThrottleSeconds)
    {
        if (!_valid || revision != _revision || !CascadesEqual(cascades, _cascades))
            return false;
        if (!castersAnimating)
            return true;   // static scene — nothing to catch up on regardless
        if (animatingThrottleSeconds <= 0f || Time.Now - _lastRenderTime >= animatingThrottleSeconds)
            return false;   // redraw now to catch up with the animation
        return true;   // reuse a still-recent render while only wind is moving
    }

    // The fresh fit differs from what's rendered (typically: the light rotated a little) but the
    // CAMERA hasn't moved and we're still inside the light-throttle window — reuse the last
    // render anyway. Unlike CanReuse's wind case, this reuse is NOT free: the fit itself is stale,
    // so the caller MUST substitute CachedCascades for the fresh fit it just computed (not use
    // the fresh one) — otherwise the lit shader would sample last frame's depth texture with THIS
    // frame's (different) light matrix, a genuine misalignment rather than mere imperceptible
    // lag. Requires the caller to have independently verified the camera is unchanged
    // (cameraStatic) and the scene hasn't (revision) — reusing a stale fit while either of those
    // moved would show visibly wrong geometry, not just a lagging shadow, since a moved camera or
    // scene needs an immediately-correct redraw the same way it always has.
    public bool CanReuseStaleFit(int revision, bool cameraStatic, float throttleSeconds) =>
        _valid && revision == _revision && cameraStatic &&
        throttleSeconds > 0f && Time.Now - _lastRenderTime < throttleSeconds;

    // Snapshots `cascades` (Clone — the caller's array is mutated in place next frame, so a
    // plain reference would silently "update" this record to next frame's values too).
    public void Record(int revision, Cascade[] cascades)
    {
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
