namespace Centauri.Rendering.DebugView;

using System.Numerics;

internal static class ColorPalette
{
    public static readonly Vector3 Camera       = new(1.0f, 0.5f, 0.0f);
    public static readonly Vector3 CameraDir    = new(1.0f, 1.0f, 1.0f);
    public static readonly Vector3 Frustum      = new(1.0f, 1.0f, 0.0f);
    public static readonly Vector3 AABBStd      = new(0.0f, 1.0f, 0.0f);
    public static readonly Vector3 AABBCulled   = new(1.0f, 0.0f, 0.0f);
    public static readonly Vector3 GridOccupied = new(0.2f, 0.5f, 1.0f);   // holds geometry
    public static readonly Vector3 GridVisited  = new(0.1f, 1.0f, 0.6f);   // in the camera query
    public static readonly Vector3 GridSelected = new(1.0f, 0.85f, 0.1f);  // cell of the selection
    public static readonly Vector3 Selected     = new(1.0f, 1.0f, 1.0f);
    public static readonly Vector3 PhysicsDynamic  = new(1.0f, 0.4f, 0.9f);  // magenta — moves
    public static readonly Vector3 PhysicsStatic   = new(0.4f, 0.7f, 1.0f);  // blue — fixed collider
    public static readonly Vector3 PhysicsVelocity = new(1.0f, 1.0f, 0.2f);  // yellow velocity arrow
}