namespace Centauri.Rendering.Postprocessing;

using Silk.NET.OpenGL;

// Off-screen HDR target. Scene draws into a multisampled RGBA16F buffer (so MSAA
// still works), then resolves into a single-sample RGBA16F texture the tonemap
// pass samples. Recreated on resize.
public sealed class HDRFramebuffer : IDisposable
{
    private readonly GL   _gl;
    private readonly uint _samples;

    private uint _msaaFbo, _msaaColor, _msaaDepth;   // render target
    private uint _resolveFbo;         // sampled by post
    private uint _width, _height;

    public uint ResolvedTexture { get; private set; }

    public HDRFramebuffer(GL gl, uint width, uint height, uint samples)
    {
        _gl = gl;
        _samples = Math.Max(1u, samples);
        Allocate(width, height);
    }

    public void Resize(uint width, uint height)
    {
        if (width == _width && height == _height) return;
        
        Destroy();
        Allocate(width, height);
    }

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
        _gl.Viewport(0, 0, _width, _height);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    // Resolve MSAA → single-sample texture; leaves default framebuffer bound.
    public void Resolve()
    {
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resolveFbo);
        _gl.BlitFramebuffer(0, 0, (int)_width, (int)_height,
                            0, 0, (int)_width, (int)_height,
                            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private unsafe void Allocate(uint width, uint height)
    {
        _width  = Math.Max(1u, width);
        _height = Math.Max(1u, height);

        // multisampled target
        _msaaFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);

        _msaaColor = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaColor);
        _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, _samples,
            InternalFormat.Rgba16f, _width, _height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, _msaaColor);

        _msaaDepth = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepth);
        _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, _samples,
            InternalFormat.DepthComponent24, _width, _height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _msaaDepth);
        CheckComplete("MSAA");

        // single-sample resolve target
        _resolveFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _resolveFbo);

        ResolvedTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, ResolvedTexture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, _width, _height, 0,
            PixelFormat.Rgba, PixelType.Float, null);
        _gl.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, ResolvedTexture, 0);
        CheckComplete("resolve");

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void CheckComplete(string which)
    {
        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            throw new Exception($"HDR {which} framebuffer incomplete: {status}");
    }

    private void Destroy()
    {
        _gl.DeleteFramebuffer(_msaaFbo);
        _gl.DeleteRenderbuffer(_msaaColor);
        _gl.DeleteRenderbuffer(_msaaDepth);
        _gl.DeleteFramebuffer(_resolveFbo);
        _gl.DeleteTexture(ResolvedTexture);
    }

    public void Dispose() => Destroy();
}