namespace SimpleTerrain.Rendering.Renderers;

using Silk.NET.OpenGL;
using System.Numerics;

using World;
using Resources;
using Utils.Geometry;

public class DebugRenderer : IDisposable
{
    private readonly GL       _gl;
    private readonly GLShader _shader;
    private readonly Mesh     _cameraMesh;

    private readonly uint _lineVao;
    private readonly uint _lineVbo;

    // tracks current GPU buffer capacity in floats — grows as needed, never shrinks
    private nuint _lineBufferCapacity = 0;

    private bool _debugStateActive = false;

    private const float CameraScale     =  0.5f;
    private const float CameraModelBase = -0.4f;
    private const float DirLineLength   = 100.0f;

    private static readonly Vector3 ColorCamera  = new(1.0f, 0.5f, 0.0f);
    private static readonly Vector3 ColorDir     = new(1.0f, 1.0f, 1.0f);
    private static readonly Vector3 ColorFrustum = new(1.0f, 1.0f, 0.0f);
    private static readonly Vector3 ColorAABB        = new(0.0f, 1.0f, 0.0f); // green  — visible
    private static readonly Vector3 ColorAABBCulled  = new(1.0f, 0.0f, 0.0f); // red    — culled

    private static readonly int[] BoxEdgeIndices =
    [
        0,1, 1,3, 3,2, 2,0,
        4,5, 5,7, 7,6, 6,4,
        0,4, 1,5, 2,6, 3,7
    ];

    public DebugRenderer(GL gl)
    {
        _gl         = gl;
        _shader     = new GLShader(gl, "Assets/Shaders/debug.vert", "Assets/Shaders/debug.frag");
        _cameraMesh = BuildCameraMesh(gl);

        (_lineVao, _lineVbo) = CreateLineBuffer();
    }

    // ── Begin / End ───────────────────────────────────────────────────────────
    // caller wraps all debug draw calls between Begin/End
    // state changes happen exactly once per frame regardless of how many things are drawn

    public void Begin(Camera camera)
    {
        if (_debugStateActive)
            throw new InvalidOperationException("DebugRenderer.Begin called twice without End.");

        _debugStateActive = true;

        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);

        _shader.Use();
        _shader.SetUniform("uView",       camera.GetViewMatrix());
        _shader.SetUniform("uProjection", camera.GetProjectionMatrix());
    }

    public void End()
    {
        if (!_debugStateActive)
            throw new InvalidOperationException("DebugRenderer.End called without Begin.");

        _debugStateActive = false;

        _gl.Enable(EnableCap.CullFace);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthMask(true);
    }

    // ── Draw calls — must be between Begin/End ────────────────────────────────

    public void DrawCameras(Scene scene)
    {
        AssertActive();
        if (!scene.DebugSettings.ShowCameras) return;
        
        var active = scene.GetActiveCamera();

        foreach (var cam in scene.Cameras)
        {
            if (cam == active) continue;
            
            DrawCameraShape(cam);
            DrawDirectionLine(cam);
            
            if (scene.DebugSettings.ShowFrustums) 
                DrawFrustum(cam);
        }
    }

    public void DrawAllAABBs(Scene scene, Frustum cullingFrustum)
    {
        AssertActive();
        if (!scene.DebugSettings.ShowBoundingBoxes) return;
    
        _shader.SetUniform("uModel", Matrix4x4.Identity);

        foreach (var entity in scene.Entities)
        {
            var bounds  = entity.GetWorldBounds();
            var culled  = !cullingFrustum.IsVisibleAABB(bounds);
        
            _shader.SetUniform("uColor", culled ? ColorAABBCulled : ColorAABB);
            DrawBoxEdges(GetBoxCorners(bounds.Min, bounds.Max));
        }
    }

    // ── Private drawing ───────────────────────────────────────────────────────

    private void DrawCameraShape(Camera cam)
    {
        var model =
            Matrix4x4.CreateScale(CameraScale) *
            Matrix4x4.CreateWorld(cam.Position, cam.Forward, cam.Up);

        _shader.SetUniform("uModel", model);
        _shader.SetUniform("uColor", ColorCamera);

        _cameraMesh.Bind();
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, _cameraMesh.IndexCount,
                DrawElementsType.UnsignedInt, (void*)0);
        }
    }

    private void DrawDirectionLine(Camera cam)
    {
        _shader.SetUniform("uModel", Matrix4x4.Identity);
        _shader.SetUniform("uColor", ColorDir);

        var tipOffset = MathF.Abs(CameraModelBase) * CameraScale;
        var start     = cam.Position + cam.Forward * tipOffset;
        var end       = start + cam.Forward * DirLineLength;

        UploadAndDrawLines(
        [
            start.X, start.Y, start.Z,
            end.X,   end.Y,   end.Z
        ], 2);
    }

    private void DrawFrustum(Camera cam)
    {
        _shader.SetUniform("uModel", Matrix4x4.Identity);
        _shader.SetUniform("uColor", ColorFrustum);
        DrawBoxEdges(cam.Frustum.GetFrustumCorners());
    }

    private void DrawBoxEdges(Vector3[] corners)
    {
        var vertices = new float[BoxEdgeIndices.Length * 3];
        for (int i = 0; i < BoxEdgeIndices.Length; i++)
        {
            var p = corners[BoxEdgeIndices[i]];
            vertices[i * 3 + 0] = p.X;
            vertices[i * 3 + 1] = p.Y;
            vertices[i * 3 + 2] = p.Z;
        }
        UploadAndDrawLines(vertices, (uint)BoxEdgeIndices.Length);
    }

    private void UploadAndDrawLines(float[] vertices, uint count)
    {
        var sizeInBytes = (nuint)(vertices.Length * sizeof(float));

        _gl.BindVertexArray(_lineVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _lineVbo);

        unsafe
        {
            fixed (float* v = vertices)
            {
                if (sizeInBytes > _lineBufferCapacity)
                {
                    // grow — reallocate with extra headroom to avoid frequent resizes
                    _lineBufferCapacity = sizeInBytes * 2;
                    _gl.BufferData(BufferTargetARB.ArrayBuffer,
                        _lineBufferCapacity, null, BufferUsageARB.DynamicDraw);
                }

                // always use SubData — only writes what we need
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, sizeInBytes, v);
            }
        }

        _gl.DrawArrays(PrimitiveType.Lines, 0, count);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AssertActive([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (!_debugStateActive)
            throw new InvalidOperationException(
                $"DebugRenderer.{caller} called outside Begin/End block.");
    }

    private (uint vao, uint vbo) CreateLineBuffer()
    {
        var vao = _gl.GenVertexArray();
        var vbo = _gl.GenBuffer();

        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        // start with no allocation — grows on first draw
        unsafe
        {
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float,
                false, 3 * sizeof(float), (void*)0);
        }

        _gl.BindVertexArray(0);
        return (vao, vbo);
    }

    private static Mesh BuildCameraMesh(GL gl)
    {
        const float b = CameraModelBase;
        float[] vertices =
        [
             0f,    0f,  0f,  0,1,0, 0,0, 1,0,0,
            -0.2f,-0.2f,  b,  0,1,0, 0,0, 1,0,0,
             0.2f,-0.2f,  b,  0,1,0, 0,0, 1,0,0,
             0.2f, 0.2f,  b,  0,1,0, 0,0, 1,0,0,
            -0.2f, 0.2f,  b,  0,1,0, 0,0, 1,0,0,
        ];
        uint[] indices = [0,1,2, 0,2,3, 0,3,4, 0,4,1, 1,2,3, 3,4,1];
        return new Mesh(gl, vertices, indices);
    }

    private static Vector3[] GetBoxCorners(Vector3 min, Vector3 max) =>
    [
        new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
        new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z),
        new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
        new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z),
    ];

    public void Dispose()
    {
        _gl.DeleteBuffer(_lineVbo);
        _gl.DeleteVertexArray(_lineVao);
        _cameraMesh.Dispose();
        _shader.Dispose();
    }
}