namespace Centauri.Graphics.Resources;

using Silk.NET.OpenGL;

// The TexParameter wrap/filter setup that was copy-pasted across GLTexture, IBLBaker
// and RenderTarget. Assumes the target texture is already bound.
public static class GLSampler
{
    // Same wrap on every axis (set R too for cubemaps via wrapR).
    public static void Set(GL gl, TextureTarget target,
        GLEnum wrap, GLEnum minFilter, GLEnum magFilter, bool wrapR = false)
    {
        Set(gl, target, wrap, wrap, minFilter, magFilter);
        if (wrapR)
            gl.TexParameter(target, TextureParameterName.TextureWrapR, (int)wrap);
    }

    // Independent S/T wrap (e.g. a panorama that repeats horizontally but clamps vertically).
    public static void Set(GL gl, TextureTarget target,
        GLEnum wrapS, GLEnum wrapT, GLEnum minFilter, GLEnum magFilter)
    {
        gl.TexParameter(target, TextureParameterName.TextureWrapS,     (int)wrapS);
        gl.TexParameter(target, TextureParameterName.TextureWrapT,     (int)wrapT);
        gl.TexParameter(target, TextureParameterName.TextureMinFilter, (int)minFilter);
        gl.TexParameter(target, TextureParameterName.TextureMagFilter, (int)magFilter);
    }
}
