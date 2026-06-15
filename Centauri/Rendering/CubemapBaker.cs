namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using System.Numerics;

using Graphics.Resources;
using Graphics.Geometry;
using Utils.Misc;

// Renders an equirectangular panorama into the six faces of a fresh cubemap (one-time, at load).
public sealed class CubemapBaker : IDisposable
{
    private readonly GL _gl;
    private readonly GLShader _shader;
    private readonly Mesh _cube;
    private readonly uint _fbo;

    private static readonly Matrix4x4 Projection =
        Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, 0.1f, 10f);

    private static readonly Matrix4x4[] Views =
    [
        Matrix4x4.CreateLookAt(Vector3.Zero, new( 1, 0, 0), new(0,-1, 0)),
        Matrix4x4.CreateLookAt(Vector3.Zero, new(-1, 0, 0), new(0,-1, 0)),
        Matrix4x4.CreateLookAt(Vector3.Zero, new( 0, 1, 0), new(0, 0, 1)),
        Matrix4x4.CreateLookAt(Vector3.Zero, new( 0,-1, 0), new(0, 0,-1)),
        Matrix4x4.CreateLookAt(Vector3.Zero, new( 0, 0, 1), new(0,-1, 0)),
        Matrix4x4.CreateLookAt(Vector3.Zero, new( 0, 0,-1), new(0,-1, 0)),
    ];

    public CubemapBaker(GL gl)
    {
        _gl = gl;
        _shader = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/equirectToCube.vert"),
            PathResolver.Resolve("Assets/Shaders/equirectToCube.frag"));
        _cube = BuildCube(gl);
        _fbo  = gl.GenFramebuffer();
    }

    public GLCubemap Bake(GLTexture equirect, int size)
    {
        var cube = GLCubemap.CreateEmpty(_gl, size);

        Span<int> vp = stackalloc int[4];
        _gl.GetInteger(GLEnum.Viewport, vp);              // save viewport

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)size, (uint)size);
        _gl.Disable(EnableCap.DepthTest);                 // direction-only — overlap is harmless
        _gl.Disable(EnableCap.CullFace);

        _shader.Use();
        _shader.SetUniform("uProjection", Projection);
        _shader.SetUniform("uEquirect", 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, equirect.Handle);

        _cube.Bind();
        for (var i = 0; i < 6; i++)
        {
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.TextureCubeMapPositiveX + i, cube.Handle, 0);

            _shader.SetUniform("uView", Views[i]);
            unsafe
            {
                _gl.DrawElements(PrimitiveType.Triangles, _cube.IndexCount,
                    DrawElementsType.UnsignedInt, (void*)0);
            }
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);      // restore
        _gl.Viewport(vp[0], vp[1], (uint)vp[2], (uint)vp[3]);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);

        cube.GenerateMipmaps();
        return cube;
    }

    private static Mesh BuildCube(GL gl)
    {
        ReadOnlySpan<float> pos =
        [
            -1,-1,-1,  1,-1,-1,  1, 1,-1, -1, 1,-1,
            -1,-1, 1,  1,-1, 1,  1, 1, 1, -1, 1, 1,
        ];

        var vertices = new float[8 * 11];   // pad to Mesh's 11-float stride
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

        return new Mesh(gl, vertices, indices);
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(_fbo);
        _cube.Dispose();
        _shader.Dispose();
    }
}