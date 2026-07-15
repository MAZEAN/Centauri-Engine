namespace Centauri.Rendering.DebugView;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.CompilerServices;

using World;
using Config;
using Graphics.Geometry;
using Utils.Geometry;
using Culling;
using Simulation.Physics;

public sealed class DebugRenderer : IDisposable
{
    private const float DirLineLength = 100.0f;
    private const float FaceAlpha     = 0.05f; // translucency of AABB side faces
    private const float GridFaceAlpha = 0.01f;   // translucent fill on cells in view this frame
    private const float GridFaceSelectedAlpha = 0.05f;   // translucent fill on cells in view this frame
    private const float VelocityVectorScale   = 0.25f;  // world units drawn per 1 m/s of LinearVelocity
    
    private readonly AppConfig _config;
    private readonly Draw _draw;
    
    private readonly Mesh _cameraMesh;

    private bool _active;

    public DebugRenderer(GL gl, AppConfig config)
    {
        _config     = config;
        _draw       = new Draw(gl);
        
        _cameraMesh = Shapes.BuildCameraMesh(gl);
    }

    // ── Begin / End ───────────────────────────────────────────────────────────
    public void Begin(Camera camera)
    {
        if (_active)
            throw new InvalidOperationException("DebugRenderer.Begin called twice without End.");

        _active = true;
        _draw.Begin(camera.GetViewMatrix(), camera.GetProjectionMatrix());
    }

    public void End()
    {
        if (!_active)
            throw new InvalidOperationException("DebugRenderer.End called without Begin.");

        _active = false;
        _draw.End();
    }

    public void DrawCameras(Scene scene)
    {
        AssertActive();

        var active = scene.Cameras.Active;

        foreach (var cam in scene.Cameras)
        {
            if (cam == active) continue;

            if (_config.Debug.ShowCameras)
            {
                DrawCameraShape(cam);
                DrawDirectionLine(cam);
            }

            if (_config.Debug.ShowFrustums)
                DrawFrustum(cam);
        }
    }

    public void DrawAllAABBs(Scene scene, Frustum cullingFrustum)
    {
        AssertActive();
        if (!_config.Debug.ShowBoundingBoxes) return;

        _draw.Model(Matrix4x4.Identity);
        
        Span<Vector3> corners = stackalloc Vector3[8];
        foreach (var entity in scene.Entities)
        {
            var bounds  = entity.GetWorldBounds();
            var culled  = !cullingFrustum.IsVisibleAABB(bounds);
            bounds.GetBoxCorners(corners);
            
            var color   = culled ? ColorPalette.AABBCulled : ColorPalette.AABBStd;

            _draw.Color(color, FaceAlpha);          // translucent fill
            _draw.Triangles(Shapes.BoxFaces(corners));

            _draw.Color(color);
            _draw.Lines(Shapes.BoxEdges(corners));
        }
    }
    
    // Wireframe box/sphere per registered RigidBody (magenta = Dynamic, blue = Static — see
    // ColorPalette), plus a yellow arrow for a Dynamic body's current LinearVelocity, scaled by
    // VelocityVectorScale. The collider shape mirrors PhysicsSystem.Register's own
    // HalfExtents/CenterOffset math exactly (that's where those fields are written), so this
    // always draws what the simulation is actually colliding against, not an approximation of it.
    public void DrawPhysicsColliders(Scene scene)
    {
        AssertActive();
        if (!_config.Debug.ShowPhysicsColliders) return;

        Span<Vector3> corners = stackalloc Vector3[8];
        foreach (var entity in scene.Entities)
        {
            if (entity.GetComponent<RigidBody>() is not { Registered: true } rb) continue;

            var t      = entity.Transform;
            var center = t.Position + Vector3.Transform(rb.CenterOffset, t.Rotation);
            var color  = rb.Kind == BodyKind.Static ? ColorPalette.PhysicsStatic : ColorPalette.PhysicsDynamic;

            _draw.Color(color);
            if (rb.Shape == BodyShape.Sphere)
            {
                _draw.Model(Matrix4x4.CreateTranslation(center));
                _draw.Lines(Shapes.SphereEdges(rb.HalfExtents.X));
            }
            else
            {
                _draw.Model(Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(center));
                new BoundingBox(-rb.HalfExtents, rb.HalfExtents).GetBoxCorners(corners);
                _draw.Lines(Shapes.BoxEdges(corners));
            }

            if (rb.Kind != BodyKind.Dynamic || rb.LinearVelocity.LengthSquared() < 1e-6f) continue;

            _draw.Model(Matrix4x4.Identity);
            _draw.Color(ColorPalette.PhysicsVelocity);
            var tip = center + rb.LinearVelocity * VelocityVectorScale;
            _draw.Lines([center.X, center.Y, center.Z, tip.X, tip.Y, tip.Z]);
        }
    }

    public void DrawSelection(Scene scene)
    {
        AssertActive();
        if (scene.Selected is not { } e || e.Model is null) return;
        
        Span<Vector3> corners = stackalloc Vector3[8];
        e.GetWorldBounds().GetBoxCorners(corners);

        _draw.Model(Matrix4x4.Identity);
        _draw.Color(ColorPalette.Selected);
        _draw.Lines(Shapes.BoxEdges(corners));
    }
    
    public void DrawCullingGrid(Scene scene, SpatialGrid grid)
    {
        AssertActive();
        if (!_config.Debug.ShowCullingGrid) return;

        _draw.Model(Matrix4x4.Identity);
        
        Span<Vector3> corners = stackalloc Vector3[8];

        for (var r = 0; r < grid.Rows; r++)
        {
            for (var c = 0; c < grid.Columns; c++)
            {
                if (grid.CellCount(c, r) == 0) continue;   // occupied cells only

                grid.CellBounds(c, r).GetBoxCorners(corners);
                var visited = grid.CellVisited(c, r);
                var color   = visited ? ColorPalette.GridVisited : ColorPalette.GridOccupied;

                if (visited)
                {
                    _draw.Color(color, GridFaceAlpha);
                    _draw.Triangles(Shapes.BoxFaces(corners));
                }
                
                _draw.Color(color);
                _draw.Lines(Shapes.BoxEdges(corners));
            } 
        }
            
        
        if (scene.Selected is { Model: not null } selected &&
            grid.TryGetCells(selected.GetWorldBounds(), out var c0, out var r0, out var c1, out var r1))
        {
            for (var r = r0; r <= r1; r++)
            {
                for (var c = c0; c <= c1; c++)
                {
                    grid.CellBounds(c, r).GetBoxCorners(corners);
                    
                    _draw.Color(ColorPalette.GridSelected, GridFaceSelectedAlpha);
                    _draw.Triangles(Shapes.BoxFaces(corners));
                    
                    _draw.Color(ColorPalette.GridSelected);
                    _draw.Lines(Shapes.BoxEdges(corners));
                }
            }
        }
    }
    
    private void DrawCameraShape(Camera cam)
    {
        var model =
            Matrix4x4.CreateScale(Shapes.CameraScale) *
            Matrix4x4.CreateWorld(cam.Position, cam.Forward, cam.Up);

        _draw.Model(model);
        _draw.Color(ColorPalette.Camera);
        _draw.DrawMesh(_cameraMesh);
    }

    private void DrawDirectionLine(Camera cam)
    {
        _draw.Model(Matrix4x4.Identity);
        _draw.Color(ColorPalette.CameraDir);

        var tipOffset = MathF.Abs(Shapes.CameraModelBase) * Shapes.CameraScale;
        var start   = cam.Position + cam.Forward * tipOffset;
        var end     = start + cam.Forward * DirLineLength;

        _draw.Lines([start.X, start.Y, start.Z, end.X, end.Y, end.Z]);
    }

    private void DrawFrustum(Camera cam)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        cam.GetFrustumCorners(corners);
        
        _draw.Model(Matrix4x4.Identity);
        _draw.Color(ColorPalette.Frustum);
        _draw.Lines(Shapes.BoxEdges(corners));
    }

    private void AssertActive([CallerMemberName] string caller = "")
    {
        if (!_active)
            throw new InvalidOperationException(
                $"DebugRenderer.{caller} called outside Begin/End block.");
    }

    public void Dispose()
    {
        _cameraMesh.Dispose();
        _draw.Dispose();
    }
}