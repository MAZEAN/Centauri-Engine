namespace Centauri.Rendering.DebugView;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.CompilerServices;

using World;
using Config;
using Graphics.Geometry;
using Utils.Geometry;
using Culling;

public sealed class DebugRenderer : IDisposable
{
    private const float DirLineLength = 100.0f;
    private const float FaceAlpha     = 0.05f; // translucency of AABB side faces
    private const float GridFaceAlpha = 0.01f;   // translucent fill on cells in view this frame
    private const float GridFaceSelectedAlpha = 0.05f;   // translucent fill on cells in view this frame
    
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

        foreach (var entity in scene.Entities)
        {
            var bounds  = entity.GetWorldBounds();
            var culled  = !cullingFrustum.IsVisibleAABB(bounds);
            var corners = bounds.GetBoxCorners();
            var color   = culled ? ColorPalette.AABBCulled : ColorPalette.AABBStd;

            _draw.Color(color, FaceAlpha);          // translucent fill
            _draw.Triangles(Shapes.BoxFaces(corners));

            _draw.Color(color);
            _draw.Lines(Shapes.BoxEdges(corners));
        }
    }
    
    public void DrawSelection(Scene scene)
    {
        AssertActive();
        if (scene.Selected is not { } e || e.Model is null) return;

        _draw.Model(Matrix4x4.Identity);
        _draw.Color(ColorPalette.Selected);
        _draw.Lines(Shapes.BoxEdges(e.GetWorldBounds().GetBoxCorners()));
    }
    
    public void DrawCullingGrid(Scene scene, SpatialGrid grid)
    {
        AssertActive();
        if (!_config.Debug.ShowCullingGrid) return;

        _draw.Model(Matrix4x4.Identity);

        for (var r = 0; r < grid.Rows; r++)
        {
            for (var c = 0; c < grid.Columns; c++)
            {
                if (grid.CellCount(c, r) == 0) continue;   // occupied cells only

                var corners = grid.CellBounds(c, r).GetBoxCorners();
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
                    var corners = grid.CellBounds(c, r).GetBoxCorners();
                    
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
        _draw.Model(Matrix4x4.Identity);
        _draw.Color(ColorPalette.Frustum);
        _draw.Lines(Shapes.BoxEdges(cam.GetFrustumCorners()));
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