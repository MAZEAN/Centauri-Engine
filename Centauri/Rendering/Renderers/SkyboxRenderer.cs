namespace Centauri.Rendering.Renderers;

using Silk.NET.OpenGL;
using System.Numerics;

using World;
using World.Components;
using Graphics.Geometry;
using Graphics.Resources;
using Utils.Misc;
using Config;

public class SkyboxRenderer : IDisposable
{
    private const float SunAngularSizeCos  = 0.99999f; 
    private const float SunGlowExponent    = 500f;    // higher = tighter halo
    
    private readonly GL       _gl;
    private readonly AppConfig _config;
    
    private readonly GLShader _shader;
    private readonly Mesh     _cube;

    public SkyboxRenderer(GL gl, AppConfig config)
    {
        _gl = gl;
        _config = config;
        
        _shader = new GLShader(gl,
            PathResolver.Resolve("Shaders/Skybox/skybox.vert"),
            PathResolver.Resolve("Shaders/Skybox/skybox.frag"));

        var (vertices, indices) = BuildCube();
        _cube = new Mesh(gl, vertices, indices);
    }

    public void Render(Scene scene)
        => Render(scene, scene.Cameras.Active.GetViewMatrix(), scene.Cameras.Active.GetProjectionMatrix());
    
    public void Render(Scene scene, Matrix4x4 view, Matrix4x4 projection)
    {
        var sky = scene.Skyboxes.Active;
        var procedural = _config.Sky.Procedural && DayNightCycle.IsDay(scene);

        if (!procedural && sky is null) return;   // textured mode needs a loaded panorama

        view.Translation = Vector3.Zero;

        SetSkyboxRenderState();

        _shader.Use();
        _shader.SetUniform("uView",       view);
        _shader.SetUniform("uProjection", projection);
        _shader.SetUniform("uMode",       procedural ? 1 : 0);
        
        if (procedural)
        {
            _shader.SetUniform("uTurbidity",    _config.Sky.Turbidity);
            _shader.SetUniform("uSkyIntensity", _config.Sky.Intensity);
        }
        else if (sky is { } s)
        {
            _shader.SetUniform("uPanorama",   0);
            _shader.SetUniform("uHdr",        s.Texture.IsHdr ? 1 : 0);
            _shader.SetUniform("uExposure",   s.Exposure);
            _shader.SetUniform("uBlackLevel", s.BlackLevel);

            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, s.Texture.Handle);
        }

        UploadSun(scene);

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
    
    private void UploadSun(Scene scene)
    {
        if (scene.Lighting.DirectionalLights.Count == 0)
        {
            _shader.SetUniform("uSunColor", Vector3.Zero);
            return;
        }

        var sun = scene.Lighting.DirectionalLights[0];
        
        var sunDir = -Vector3.Normalize(sun.Direction);

        _shader.SetUniform("uSunDir",          sunDir);
        _shader.SetUniform("uSunColor",        sun.Color * sun.Intensity);
        _shader.SetUniform("uSunAngularSize",  SunAngularSizeCos);
        _shader.SetUniform("uSunGlowExponent", SunGlowExponent);
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