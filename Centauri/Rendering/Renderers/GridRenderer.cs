namespace Centauri.Rendering.Renderers;

using Silk.NET.OpenGL;
using World;
using Config;
using Resources;
using Utils.Misc;

public class GridRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly GLShader _shader;
    private readonly Mesh _mesh;
    private readonly WindowConfig _config;

    public GridRenderer(GL gl, WindowConfig config)
    {
        _gl     = gl;
        _config = config;
        _shader = new GLShader(gl,
            AssetPath.Resolve("Assets/Shaders/grid.vert"),
            AssetPath.Resolve("Assets/Shaders/grid.frag"));

        // fullscreen quad in NDC space (-1 to 1)
        // stride is 11 floats to match updated Mesh layout
        // normal and tangent are placeholders — grid shader doesn't use them
        float[] vertices =
        [
            // position       normal        uv        tangent
            -1f,  1f,  0f,  0f, 0f, 1f,  0f, 1f,  1f, 0f, 0f,
            -1f, -1f,  0f,  0f, 0f, 1f,  0f, 0f,  1f, 0f, 0f,
             1f,  1f,  0f,  0f, 0f, 1f,  1f, 1f,  1f, 0f, 0f,
             1f, -1f,  0f,  0f, 0f, 1f,  1f, 0f,  1f, 0f, 0f,
        ];

        uint[] indices = [0, 1, 2, 2, 1, 3];
        _mesh = new Mesh(gl, vertices, indices);
    }

    public void Render(Scene scene)
    {
        SetDebugRenderState();

        var camera = scene.GetActiveCamera();
        
        _shader.Use();
        _shader.SetUniform("uView",        camera.GetViewMatrix());
        _shader.SetUniform("uProjection",  camera.GetProjectionMatrix());
        _shader.SetUniform("uCameraPos",   camera.Position);

        _mesh.Bind();
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, 6,
                DrawElementsType.UnsignedInt, (void*)0);
        }

        RestoreRenderState();
    }

    private void SetDebugRenderState()
    {
        _gl.DepthFunc(GLEnum.Lequal);
    }

    private void RestoreRenderState()
    {
        _gl.DepthFunc(DepthFunction.Less);
    }

    public void Dispose()
    {
        _mesh.Dispose();
        _shader.Dispose();
    }
}