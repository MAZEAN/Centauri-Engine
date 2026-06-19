namespace Centauri.Rendering.IBL;

using Silk.NET.OpenGL;
using System.Numerics;

using Graphics.Resources;
using Utils.Misc;
using Config;

// Precomputes IBL maps from an equirectangular HDR environment, plus a one-time
// global BRDF LUT. Standard split-sum / LearnOpenGL flow.
public sealed class IBLBaker : IDisposable
{
    private readonly GL _gl;
    private readonly IBLConfig _config;
    
    private readonly uint _fbo, _rbo, _cubeVao, _cubeVbo, _quadVao;
    private readonly GLShader _toCube, _irradiance, _prefilter, _brdf;
    private readonly Matrix4x4 _proj;
    private readonly Matrix4x4[] _views;
    
    private readonly List<uint> _baked = new();

    public uint BrdfLut { get; }
    public int MaxReflectionLod => _config.PrefilterMips - 1;

    public IBLBaker(GL gl, IBLConfig config)
    {
        _gl = gl;
        _config = config;

        _fbo = gl.GenFramebuffer();
        _rbo = gl.GenRenderbuffer();
        
        (_cubeVao, _cubeVbo) = CreateCube();
        
        _quadVao = gl.GenVertexArray();   // empty VAO for the fullscreen-triangle draws

        _toCube     = Load("equirect_to_cubemap");
        _irradiance = Load("irradiance");
        _prefilter  = Load("prefilter");
        _brdf = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Post/post.vert"),
            PathResolver.Resolve("Assets/Shaders/IBL/brdf.frag"));

        _proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, 0.1f, 10f);
        _views = CaptureViews();

        BrdfLut = BakeBrdf();
    }
    
    public unsafe (uint irradiance, uint prefiltered) Bake(GLTexture equirect, float exposure)
    {
        _gl.Disable(EnableCap.CullFace);
        
        try
        {
            // 1) equirect → env cubemap (+ mips, used by the prefilter pass)
            var env = CreateCubemap(_config.EnvSize, mips: true);
            _toCube.Use();
            _toCube.SetUniform("uProjection", _proj);
            _toCube.SetUniform("uEquirect", 0);
            _toCube.SetUniform("uExposure", exposure); 
            
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, equirect.Handle);
        
            RenderToCube(env, _config.EnvSize, 0, _toCube);
            _gl.BindTexture(TextureTarget.TextureCubeMap, env);
            _gl.GenerateMipmap(TextureTarget.TextureCubeMap);
    
            // 2) irradiance
            var irr = CreateCubemap(_config.IrradianceSize, mips: false);
            _irradiance.Use();
            _irradiance.SetUniform("uProjection", _proj);
            _irradiance.SetUniform("uEnv", 0);
            _irradiance.SetUniform("uMaxRadiance", _config.MaxRadiance); 
        
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.TextureCubeMap, env);
            RenderToCube(irr, _config.IrradianceSize, 0, _irradiance);
    
            // 3) prefilter (one render per mip / roughness)
            var pre = CreateCubemap(_config.PrefilterSize, mips: true);
            _prefilter.Use();
            _prefilter.SetUniform("uProjection", _proj);
            _prefilter.SetUniform("uEnv", 0);
            _prefilter.SetUniform("uResolution", _config.EnvSize);
            _prefilter.SetUniform("uMaxRadiance", _config.MaxRadiance);
        
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.TextureCubeMap, env);
        
            for (var mip = 0; mip < _config.PrefilterMips; mip++)
            {
                var size = (uint)(_config.PrefilterSize * MathF.Pow(0.5f, mip));
                _prefilter.SetUniform("uRoughness", mip / (float)(_config.PrefilterMips - 1));
                RenderToCube(pre, size, mip, _prefilter);
            }
    
            _gl.DeleteTexture(env);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            
            _baked.Add(irr);
            _baked.Add(pre);
            
            return (irr, pre);
        }
        finally
        {
            _gl.Enable(EnableCap.CullFace);
        }
    }

    private unsafe void RenderToCube(uint cubemap, uint size, int mip, GLShader shader)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _rbo);
        
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24,
            size, size);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _rbo);
        _gl.Viewport(0, 0, size, size);

        _gl.BindVertexArray(_cubeVao);
        for (var i = 0; i < 6; i++)
        {
            shader.SetUniform("uView", _views[i]);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + i), cubemap, mip);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
        }
        _gl.BindVertexArray(0);
    }

    private unsafe uint BakeBrdf()
    {
        var lut = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, lut);
        _gl.TexImage2D(TextureTarget.Texture2D, 0,
            InternalFormat.RG16f, _config.BrdfSize, _config.BrdfSize, 0, PixelFormat.RG, PixelType.Float, null);
        
        foreach (var (k, v) in ClampLinear()) _gl.TexParameter(TextureTarget.Texture2D, k, v);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _rbo);
        
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
            InternalFormat.DepthComponent24, _config.BrdfSize, _config.BrdfSize);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, lut, 0);
        
        _gl.Viewport(0, 0, _config.BrdfSize, _config.BrdfSize);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _brdf.Use();
        
        _gl.BindVertexArray(_quadVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        
        return lut;
    }

    private unsafe uint CreateCubemap(uint size, bool mips)
    {
        var id = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.TextureCubeMap, id);
        
        for (var i = 0; i < 6; i++)
            _gl.TexImage2D((TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + i), 0,
                InternalFormat.Rgb16f, size, size, 0, PixelFormat.Rgb, PixelType.Float, null);

        _gl.TexParameter(TextureTarget.TextureCubeMap,
            TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.TextureCubeMap,
            TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.TextureCubeMap,
            TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.TextureCubeMap,
            TextureParameterName.TextureMinFilter, (int)(mips ? GLEnum.LinearMipmapLinear : GLEnum.Linear));
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        
        if (mips) 
            _gl.GenerateMipmap(TextureTarget.TextureCubeMap);
        
        return id;
    }

    private static (TextureParameterName, int)[] ClampLinear() =>
    [
        (TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge),
        (TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge),
        (TextureParameterName.TextureMinFilter, (int)GLEnum.Linear),
        (TextureParameterName.TextureMagFilter, (int)GLEnum.Linear),
    ];

    private GLShader Load(string name) => new(_gl,
        PathResolver.Resolve("Assets/Shaders/IBL/cube.vert"),
        PathResolver.Resolve($"Assets/Shaders/IBL/{name}.frag"));

    private static Matrix4x4[] CaptureViews()
    {
        var o = Vector3.Zero;
        return
        [
            Matrix4x4.CreateLookAt(o, new( 1, 0, 0), new(0,-1, 0)),
            Matrix4x4.CreateLookAt(o, new(-1, 0, 0), new(0,-1, 0)),
            Matrix4x4.CreateLookAt(o, new( 0, 1, 0), new(0, 0, 1)),
            Matrix4x4.CreateLookAt(o, new( 0,-1, 0), new(0, 0,-1)),
            Matrix4x4.CreateLookAt(o, new( 0, 0, 1), new(0,-1, 0)),
            Matrix4x4.CreateLookAt(o, new( 0, 0,-1), new(0,-1, 0)),
        ];
    }

    private unsafe (uint vao, uint vbo) CreateCube()
    {
        float[] v =
        [ // 36 positions
            -1,-1,-1, -1,-1, 1, -1, 1, 1, -1, 1, 1, -1, 1,-1, -1,-1,-1,
             1,-1,-1,  1, 1,-1,  1, 1, 1,  1, 1, 1,  1,-1, 1,  1,-1,-1,
            -1,-1,-1,  1,-1,-1,  1,-1, 1,  1,-1, 1, -1,-1, 1, -1,-1,-1,
            -1, 1,-1, -1, 1, 1,  1, 1, 1,  1, 1, 1,  1, 1,-1, -1, 1,-1,
            -1,-1,-1, -1, 1,-1,  1, 1,-1,  1, 1,-1,  1,-1,-1, -1,-1,-1,
            -1,-1, 1,  1,-1, 1,  1, 1, 1,  1, 1, 1, -1, 1, 1, -1,-1, 1
        ];
        
        var vao = _gl.GenVertexArray(); var vbo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        
        fixed (float* p = v) _gl.BufferData(BufferTargetARB.ArrayBuffer,
            (nuint)(v.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
        
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float,
            false, 3 * sizeof(float), (void*)0);
        _gl.BindVertexArray(0);
        
        return (vao, vbo);
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(_fbo); 
        _gl.DeleteRenderbuffer(_rbo);
        _gl.DeleteVertexArray(_cubeVao); 
        
        _gl.DeleteBuffer(_cubeVbo); 
        _gl.DeleteVertexArray(_quadVao);
        _gl.DeleteTexture(BrdfLut);
        
        _toCube.Dispose(); 
        _irradiance.Dispose(); 
        _prefilter.Dispose(); 
        _brdf.Dispose();
        
        foreach (var t in _baked) 
            _gl.DeleteTexture(t);
        _baked.Clear();
    }
}