namespace Centauri.Rendering.Renderers;

using Silk.NET.OpenGL;
using System.Numerics;

using World;
using Graphics.Resources;
using Utils.Misc;
using Graphics.Geometry;

public class SkyboxRenderer : IDisposable
{
    private readonly GL       _gl;
    private readonly GLShader _shader;
    private readonly Mesh     _cube;

    public SkyboxRenderer(GL gl)
    {
        _gl = gl;
        _shader = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/skybox.vert"),
            PathResolver.Resolve("Assets/Shaders/skybox.frag"));

        var (vertices, indices) = BuildCube();
        _cube = new Mesh(gl, vertices, indices);
    }

    public void Render(Scene scene)
    {
        if (scene.Skyboxes.Active is not { } cubemap) return;   // scene has no skybox — nothing to draw

        var camera = scene.Cameras.Active;

        var view = camera.GetViewMatrix();
        view.Translation = Vector3.Zero;        // rotation only — sky doesn't translate

        SetSkyboxRenderState();

        _shader.Use();
        _shader.SetUniform("uView",       view);
        _shader.SetUniform("uProjection", camera.GetProjectionMatrix());
        _shader.SetUniform("uSkybox",     0);

        cubemap.Bind(TextureUnit.Texture0);
        _cube.Bind();
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, _cube.IndexCount,
                DrawElementsType.UnsignedInt, (void*)0);
        }

        ResetSkyboxRenderState();
    }

    private static (float[] vertices, uint[] indices) BuildCube()
    {
        ReadOnlySpan<float> pos =
        [
            -1,-1,-1,  1,-1,-1,  1, 1,-1, -1, 1,-1,
            -1,-1, 1,  1,-1, 1,  1, 1, 1, -1, 1, 1,
        ];

        var vertices = new float[8 * 11];          // pad to Mesh's 11-float stride
        for (var i = 0; i < 8; i++)
        {
            vertices[i * 11 + 0] = pos[i * 3 + 0];
            vertices[i * 11 + 1] = pos[i * 3 + 1];
            vertices[i * 11 + 2] = pos[i * 3 + 2];
        }

        uint[] indices =
        [
            0,1,2, 2,3,0,  4,5,6, 6,7,4,  0,3,7, 7,4,0,
            1,2,6, 6,5,1,  0,1,5, 5,4,0,  3,2,6, 6,7,3,
        ];

        return (vertices, indices);
    }

    private void SetSkyboxRenderState()
    {
        _gl.DepthFunc(GLEnum.Lequal);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
    }

    private void ResetSkyboxRenderState()
    {
        _gl.Enable(EnableCap.CullFace);
        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less);
    }

    public void Dispose()
    {
        _cube.Dispose();
        _shader.Dispose();      // cubemap is owned by ResourceSystem, not here
    }
}