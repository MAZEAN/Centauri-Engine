namespace Centauri.Rendering.Renderers;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.CompilerServices;

using World;
using Config;
using Graphics.Geometry;
using Utils.Geometry;

public sealed class DebugRenderer : IDisposable
{
    private readonly AppConfig _config;
    private readonly DebugView.Draw _draw;
    private readonly Mesh _cameraMesh;

    private bool _active;

    private const float DirLineLength = 100.0f;
    private const float FaceAlpha     = 0.05f; // translucency of AABB side faces

    private static readonly Vector3 CameraColor     = new(1.0f, 0.5f, 0.0f);
    private static readonly Vector3 DirColor        = new(1.0f, 1.0f, 1.0f);
    private static readonly Vector3 FrustumColor    = new(1.0f, 1.0f, 0.0f);
    private static readonly Vector3 AABBColor       = new(0.0f, 1.0f, 0.0f);
    private static readonly Vector3 AABBCulledColor = new(1.0f, 0.0f, 0.0f);
    private static readonly Vector3 SelectedColor   = new(1.0f, 1.0f, 1.0f);

    public DebugRenderer(GL gl, AppConfig config)
    {
        _config     = config;
        _draw       = new DebugView.Draw(gl);
        _cameraMesh = DebugView.Shapes.BuildCameraMesh(gl);
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

    // ── Draw calls — must be between Begin/End ────────────────────────────────
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
            var color   = culled ? AABBCulledColor : AABBColor;

            _draw.Color(color, FaceAlpha);          // translucent fill
            _draw.Triangles(DebugView.Shapes.BoxFaces(corners));

            _draw.Color(color);
            _draw.Lines(DebugView.Shapes.BoxEdges(corners));
        }
    }
    
    public void DrawSelection(Scene scene)
    {
        AssertActive();
        if (scene.Selected is not { } e || e.Model is null) return;

        _draw.Model(Matrix4x4.Identity);
        _draw.Color(SelectedColor);
        _draw.Lines(DebugView.Shapes.BoxEdges(e.GetWorldBounds().GetBoxCorners()));
    }

    // ── Private drawing ───────────────────────────────────────────────────────
    private void DrawCameraShape(Camera cam)
    {
        var model =
            Matrix4x4.CreateScale(DebugView.Shapes.CameraScale) *
            Matrix4x4.CreateWorld(cam.Position, cam.Forward, cam.Up);

        _draw.Model(model);
        _draw.Color(CameraColor);
        _draw.DrawMesh(_cameraMesh);
    }

    private void DrawDirectionLine(Camera cam)
    {
        _draw.Model(Matrix4x4.Identity);
        _draw.Color(DirColor);

        var tipOffset = MathF.Abs(DebugView.Shapes.CameraModelBase) * DebugView.Shapes.CameraScale;
        var start     = cam.Position + cam.Forward * tipOffset;
        var end       = start + cam.Forward * DirLineLength;

        _draw.Lines([start.X, start.Y, start.Z, end.X, end.Y, end.Z]);
    }

    private void DrawFrustum(Camera cam)
    {
        _draw.Model(Matrix4x4.Identity);
        _draw.Color(FrustumColor);
        _draw.Lines(DebugView.Shapes.BoxEdges(cam.GetFrustumCorners()));
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