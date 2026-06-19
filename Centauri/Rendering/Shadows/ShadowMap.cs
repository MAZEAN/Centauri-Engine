namespace Centauri.Rendering.Shadows;

using Silk.NET.OpenGL;

// Depth-only render target rendered from the light's POV.
// CSM allocates one per cascade (or swaps Texture2D for a 2D array).
public sealed class ShadowMap : IDisposable
{
    private readonly GL   _gl;
    private readonly uint _fbo;
    public uint DepthTexture { get; }
    public uint Size { get; }

    public unsafe ShadowMap(GL gl, uint size)
    {
        _gl  = gl;
        Size = size;

        DepthTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, DepthTexture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24,
            size, size, 0, PixelFormat.DepthComponent, PixelType.Float, null);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToBorder);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToBorder);

        // outside the light frustum reads depth 1.0 => fully lit (never self-shadowed)
        Span<float> border = [1f, 1f, 1f, 1f];
        fixed (float* b = border)
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, b);

        _fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, DepthTexture, 0);
        gl.DrawBuffer(DrawBufferMode.None);   // no color buffer
        gl.ReadBuffer(ReadBufferMode.None);

        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            throw new Exception("Shadow framebuffer incomplete");

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, Size, Size);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteTexture(DepthTexture);
    }
}