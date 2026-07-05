namespace Centauri.Rendering.Shadows;

using Silk.NET.OpenGL;

// Depth Texture2DArray — one layer per cascade. Replaces ShadowMap for CSM.
// Two copies of the same depth data are kept: DepthTexture carries the GL_COMPARE_REF_TO_TEXTURE
// mode, so the lit shader can sample it as a sampler2DArrayShadow — hardware bilinear-filtered
// PCF, comparing all 4 nearest texels and blending the *results* into one smooth fractional value
// per tap. RawDepthTexture is a plain, uncompared copy for PCSS's blocker search, which needs
// actual depth values rather than a pass/fail bit. A single texture can't serve both roles at
// once (a compare-enabled texture is "incomplete" when sampled as a non-shadow sampler), so
// SyncRawDepth() copies the freshly-rendered cascade layers over after each shadow pass.
public sealed class ShadowArray : IDisposable
{
    private readonly GL   _gl;
    private readonly uint _fbo;
    
    public uint DepthTexture { get; }
    public uint RawDepthTexture { get; }
    public uint Size   { get; }
    public int  Layers { get; }

    public unsafe ShadowArray(GL gl, uint size, int layers)
    {
        _gl = gl; 
        Size = size;
        Layers = layers;

        DepthTexture    = CreateDepthTexture(gl, size, layers, compare: true);
        RawDepthTexture = CreateDepthTexture(gl, size, layers, compare: false);

        _fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        gl.DrawBuffer(DrawBufferMode.None);
        gl.ReadBuffer(ReadBufferMode.None);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private static unsafe uint CreateDepthTexture(GL gl, uint size, int layers, bool compare)
    {
        var tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, tex);
        gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.DepthComponent24,
            size, size, (uint)layers, 0, PixelFormat.DepthComponent, PixelType.Float, null);

        // Linear: for the compare texture this is what gives the hardware 2x2 PCF blend: for
        // the raw copy, foliage casters are alpha-tested cutouts (see depth.frag) whose stored
        // depth flips hard between "leaf" and "gap" at every leaf edge, and nearest-sampling
        // that unblended would turn the intended soft, dappled leaf-shadow look into coarse
        // screen-door noise.
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToBorder);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToBorder);

        if (compare)
        {
            // hardware depth comparison — turns this into a sampler2DArrayShadow source.
            // LEQUAL matches "lit when current <= closest".
            gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareMode,
                (int)GLEnum.CompareRefToTexture);
            gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
        }

        Span<float> border = [1f, 1f, 1f, 1f];
        fixed (float* b = border)
            gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureBorderColor, b);

        return tex;
    }

    // attach one cascade layer of the compare texture and clear it
    public void BindLayer(int layer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment, DepthTexture, 0, layer);
        _gl.Viewport(0, 0, Size, Size);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
    }
    
    // duplicate the freshly-rendered compare texture into the raw one for PCSS's blocker
    // search. Only needs to run when the cascades were actually re-rendered this frame — the
    // caller gates this behind the same cache check that gates the render itself.
    public void SyncRawDepth() =>
        _gl.CopyImageSubData(
            DepthTexture, CopyImageSubDataTarget.Texture2DArray, 0, 0, 0, 0,
            RawDepthTexture, CopyImageSubDataTarget.Texture2DArray, 0, 0, 0, 0,
            Size, Size, (uint)Layers);

    public void Dispose()
    {
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteTexture(DepthTexture);
        _gl.DeleteTexture(RawDepthTexture);
    }
}