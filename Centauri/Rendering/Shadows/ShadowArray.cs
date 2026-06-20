namespace Centauri.Rendering.Shadows;

using Silk.NET.OpenGL;

// Depth Texture2DArray — one layer per cascade. Replaces ShadowMap for CSM.
public sealed class ShadowArray : IDisposable
{
    private readonly GL   _gl;
    private readonly uint _fbo;
    public uint DepthTexture { get; }
    public uint Size   { get; }
    public int  Layers { get; }

    public unsafe ShadowArray(GL gl, uint size, int layers)
    {
        _gl = gl; Size = size; Layers = layers;

        DepthTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, DepthTexture);
        gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.DepthComponent24,
            size, size, (uint)layers, 0, PixelFormat.DepthComponent, PixelType.Float, null);

        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToBorder);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToBorder);
        Span<float> border = [1f, 1f, 1f, 1f];
        fixed (float* b = border)
            gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureBorderColor, b);

        _fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        gl.DrawBuffer(DrawBufferMode.None);
        gl.ReadBuffer(ReadBufferMode.None);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    // attach one cascade layer and clear it
    public void BindLayer(int layer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment, DepthTexture, 0, layer);
        _gl.Viewport(0, 0, Size, Size);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteTexture(DepthTexture);
    }
}