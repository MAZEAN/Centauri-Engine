namespace Centauri.Rendering.Shadows;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Utils.Misc;
using Graphics.Resources;

public sealed class ShadowMapper : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    private ShadowArray _maps;
    private readonly GLShader _depth;

    public bool Active { get; private set; }
    public uint DepthTexture => _maps.DepthTexture;

    public Matrix4x4[] LightMatrices { get; private set; } = [];  // proj·view per cascade (numerics order = View*Proj)
    public float[]     SplitDepths   { get; private set; } = [];  // view-space far depth per cascade

    public ShadowMapper(GL gl, AppConfig config)
    {
        _gl = gl;
        _config = config;
        _maps = new ShadowArray(gl, config.Shadows.Size, config.Shadows.CascadeCount);
        _depth = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Shadow/depth.vert"),
            PathResolver.Resolve("Assets/Shaders/Shadow/depth.frag"));
    }

    public void Render(Scene scene)
    {
        Active = false;
        if (!_config.Shadows.Enabled) return;

        // realloc on resolution OR cascade-count change
        if (_maps.Size != _config.Shadows.Size || _maps.Layers != _config.Shadows.CascadeCount)
        {
            _maps.Dispose();
            _maps = new ShadowArray(_gl, _config.Shadows.Size, _config.Shadows.CascadeCount);
        }

        if (scene.Lighting.DirectionalLights.Count == 0) return;

        var dir    = Vector3.Normalize(scene.Lighting.DirectionalLights[0].Direction);
        var camera = scene.Cameras.Active;

        ComputeCascades(camera, dir);   // fills LightMatrices + SplitDepths

        _gl.Disable(EnableCap.CullFace);
        for (var c = 0; c < _config.Shadows.CascadeCount; c++)
        {
            _maps.BindLayer(c);
            _depth.Use();
            _depth.SetUniform("uLightMatrix", LightMatrices[c]);

            foreach (var entity in scene.Entities)
            {
                if (!entity.Enabled || entity.Model is not { } model) continue;
                _depth.SetUniform("uModel", entity.Transform.WorldMatrix);
                foreach (var mesh in model.Meshes)
                {
                    mesh.Bind();
                    unsafe
                    {
                        _gl.DrawElements(PrimitiveType.Triangles, mesh.IndexCount,
                            DrawElementsType.UnsignedInt, (void*)0);
                    }
                }
            }
        }
        _gl.Enable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        Active = true;
    }
    private void ComputeCascades(Camera camera, Vector3 dir)
    {
        int n = _config.Shadows.CascadeCount;
        LightMatrices = new Matrix4x4[n];
        SplitDepths   = new float[n];

        float near = _config.Camera.Near;
        float far  = MathF.Min(_config.Shadows.Distance, _config.Camera.Far);   // shadows extend to Distance

        // full camera frustum corners (world space), unprojected from NDC
        Matrix4x4.Invert(camera.GetViewMatrix() * camera.GetProjectionMatrix(), out var invVP);
        Span<Vector3> full = stackalloc Vector3[8];
        int k = 0;
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    var ndc = new Vector4(x * 2 - 1, y * 2 - 1, z * 2 - 1, 1f);   // GL z in [-1,1]
                    var w   = Vector4.Transform(ndc, invVP);
                    full[k++] = new Vector3(w.X, w.Y, w.Z) / w.W;
                }

        float prevSplit = near;
        for (int c = 0; c < n; c++)
        {
            // logarithmic/uniform blended split distance (PSSM)
            float p   = (c + 1) / (float)n;
            float log = near * MathF.Pow(far / near, p);
            float uni = near + (far - near) * p;
            float split = _config.Shadows.SplitLambda * log + (1 - _config.Shadows.SplitLambda) * uni;
            SplitDepths[c] = split;

            // interpolate the slice corners along each frustum edge (z is linear along edges)
            float t0 = (prevSplit - near) / (far - near);
            float t1 = (split     - near) / (far - near);
            
            Span<Vector3> corners = stackalloc Vector3[8];
            for (int i = 0; i < 4; i++)
            {
                var rayN = full[i * 2 + 0];                 // near-plane corner i
                var rayF = full[i * 2 + 1];                 // far-plane  corner i
                var edge = rayF - rayN;
                corners[i + 0] = rayN + edge * t0;          // slice near
                corners[i + 4] = rayN + edge * t1;          // slice far
            }

            // center + light view looking down the light dir
            var center = Vector3.Zero;
            foreach (var p2 in corners) center += p2;
            center /= 8f;

            var lightView = Matrix4x4.CreateLookAt(center - dir, center, Vector3.UnitY);

            // fit ortho to corners in light space
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p2 in corners)
            {
                var ls = Vector3.Transform(p2, lightView);
                min = Vector3.Min(min, ls);
                max = Vector3.Max(max, ls);
            }

            // pull the near plane back so occluders behind the slice still cast
            float zPad = (max.Z - min.Z) * 0.5f;            // tune; or a config "z multiplier"
            var lightProj = Matrix4x4.CreateOrthographicOffCenter(
                min.X, max.X, min.Y, max.Y, -max.Z - zPad, -min.Z + zPad);

            LightMatrices[c] = lightView * lightProj;        // numerics order; GLSL: uLightMatrix * pos
            prevSplit = split;
        }
    }

    public void Dispose() { _maps.Dispose(); _depth.Dispose(); }
}