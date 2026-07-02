namespace Centauri.Rendering.Targets;

using Silk.NET.OpenGL;

using Graphics.Resources;

// A reusable single-sample off-screen target: N float color textures plus an optional
// sampleable depth texture. Unlike HDRFramebuffer (which is multisampled and bespoke for
// the lit scene), this is what screen-space passes — the geometry prepass, SSAO, SSR —
// render into and sample from. Recreated on resize.
public sealed class RenderTarget : IDisposable
{
    private readonly GL _gl;
    private readonly InternalFormat[] _colorFormats;
    private readonly bool _withDepth;
    private readonly GLEnum _filter;

    private uint _fbo;
    public uint   Framebuffer   => _fbo;   // for MSAA blit-resolve into this target

    public uint[] ColorTextures { get; private set; } = [];
    public uint   DepthTexture  { get; private set; }

    public uint Width { get; private set; }
    public uint Height { get; private set; }

    public RenderTarget(GL gl, uint width, uint height,
        InternalFormat[] colorFormats, bool withDepth, GLEnum filter = GLEnum.Nearest)
    {
        _gl = gl;
        _colorFormats = colorFormats;
        _withDepth = withDepth;
        _filter = filter;
        
        Allocate(width, height);
    }

    public void Resize(uint width, uint height)
    {
        if (width == Width && height == Height) return;
        
        Destroy();
        Allocate(width, height);
    }

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, Width, Height);
    }

    public void Clear(float r = 0f, float g = 0f, float b = 0f, float a = 1f)
    {
        _gl.ClearColor(r, g, b, a);

        var mask = ClearBufferMask.ColorBufferBit;
        if (_withDepth) 
            mask |= ClearBufferMask.DepthBufferBit;
        
        _gl.Clear(mask);
    }

    private unsafe void Allocate(uint width, uint height)
    {
        Width  = Math.Max(1u, width);
        Height = Math.Max(1u, height);

        _fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        ColorTextures = new uint[_colorFormats.Length];
        Span<GLEnum> drawBuffers = stackalloc GLEnum[_colorFormats.Length];

        for (var i = 0; i < _colorFormats.Length; i++)
        {
            var tex = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, tex);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, _colorFormats[i], Width, Height, 0,
                PixelFormat.Rgba, PixelType.Float, null);
            GLSampler.Set(_gl, TextureTarget.Texture2D, GLEnum.ClampToEdge, _filter, _filter);

            var attachment = FramebufferAttachment.ColorAttachment0 + i;
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, attachment, TextureTarget.Texture2D, tex, 0);
            ColorTextures[i] = tex;
            drawBuffers[i] = (GLEnum)attachment;
        }

        if (_colorFormats.Length > 1)
            fixed (GLEnum* p = drawBuffers)
                _gl.DrawBuffers((uint)drawBuffers.Length, p);

        if (_withDepth)
        {
            DepthTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, DepthTexture);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent32f, Width, Height, 0,
                PixelFormat.DepthComponent, PixelType.Float, null);
            GLSampler.Set(_gl, TextureTarget.Texture2D, GLEnum.ClampToEdge, GLEnum.Nearest, GLEnum.Nearest);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D, DepthTexture, 0);
        }

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            throw new Exception($"RenderTarget framebuffer incomplete: {status}");

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void Destroy()
    {
        _gl.DeleteFramebuffer(_fbo);
        foreach (var tex in ColorTextures)
            _gl.DeleteTexture(tex);
        
        if (_withDepth)
            _gl.DeleteTexture(DepthTexture);
    }

    public void Dispose() => Destroy();
}
