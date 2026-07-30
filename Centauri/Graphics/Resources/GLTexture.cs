namespace Centauri.Graphics.Resources;

using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using HighDynamicRange;

public sealed class TextureData
{
    public bool    IsHdr;
    public int     Width;
    public int     Height;

    public byte[]?  Ldr;   // RGBA8, vertically flipped
    public float[]? Hdr;   // RGB float

    // Populated by GLTexture.CompressIfEligible — pure CPU work, deliberately *not* done inside
    // GLTexture's own constructor (see that method's comment for why: it needs to run wherever
    // Decode/DecodeWithOpacity themselves ran, so ResourceSystem's background decode tasks can
    // do it off the GL thread, in parallel with every other texture, the same way decode already
    // was). Null means "upload raw Ldr/Hdr as before" — either compression wasn't eligible, or
    // nothing has attempted it yet.
    internal List<BlockCompression.Level>? CompressedLevels;
    internal bool CompressedHasAlpha;
}

public class GLTexture : GLResource
{
    private const GLEnum               MaxSupportedAniso = (GLEnum)0x84FF;
    private const TextureParameterName TextureMaxAniso   = (TextureParameterName)0x84FE;
    private const float                AnisoRequest      = 8f;

    // Below this, a 4x4 BC1/BC3 block's fixed 8/16-byte cost isn't worth it — a handful of these
    // exist already (ResourceSystem's 1x1 default-white fallback texture).
    private const int MinCompressibleSize = 4;

    private static readonly List<GLTexture> Anisotropic = [];
    private static bool _anisoEnabled = true;

    // GL_EXT_texture_compression_s3tc — checked once against the live context and cached, since
    // it can't change mid-session. Must be warmed (WarmCompressionSupport) from the GL thread
    // before any background decode work starts: CompressIfEligible needs the answer but runs on
    // ResourceSystem's Task.Run decode workers, which have no GL context of their own to query it
    // from.
    private static bool? _s3tcSupported;

    private float _maxAniso = 1f;   // driver-clamped per-texture ceiling (>= 1)
    public bool IsHdr { get; }

    // True when this texture's data lives on the GPU as BC1 (opaque) or BC3 (alpha) rather than
    // raw RGBA8 — see UploadCompressed and Docs/Documentation/TextureCompression.md.
    public bool IsCompressed { get; private set; }

    // Estimated GPU bytes this texture (base level + its full mip chain) occupies — GLTexture
    // never queries the driver for actual allocation, so this is computed from the upload path
    // itself: the exact encoded size for a compressed texture, or width*height*4 scaled by 4/3 to
    // account for the ~33% a full RGBA8 mip chain adds on top of the base level for an
    // uncompressed one. Good enough for the budget visibility StatsOverlay's "Textures" section
    // shows — not a substitute for an actual driver query, which Silk.NET's GL binding has no
    // portable way to make (glGetTexLevelParameteriv's *_COMPRESSED_IMAGE_SIZE query would work
    // per-level, but summing a whole mip chain that way is a lot of round trips for a number this
    // is only ever used to eyeball).
    public long ApproxBytes { get; private set; }

    public static long TotalApproxBytes         { get; private set; }
    public static int  CompressedTextureCount   { get; private set; }
    public static int  UncompressedTextureCount { get; private set; }

    public unsafe GLTexture(GL gl, TextureData data) : base(gl)
    {
        Handle = Gl.GenTexture();
        Gl.BindTexture(TextureTarget.Texture2D, Handle);

        if (data.IsHdr)
        {
            IsHdr = true;
            fixed (float* p = data.Hdr)
                Gl.TexImage2D(
                    TextureTarget.Texture2D, 0, InternalFormat.Rgb16f,
                    (uint)data.Width, (uint)data.Height, 0,
                    PixelFormat.Rgb, PixelType.Float, p);

            ApproxBytes = (long)data.Width * data.Height * 6; // RGB16F, no mip chain (see SetParameters)
            SetParameters(hdr: true);
        }
        else if (data.CompressedLevels is { } levels)
        {
            UploadCompressed(levels, data.CompressedHasAlpha);
            SetParameters(hdr: false, skipGenerateMipmap: true);
        }
        else
        {
            fixed (byte* p = data.Ldr)
                Gl.TexImage2D(
                    TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                    (uint)data.Width, (uint)data.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, p);

            ApproxBytes = (long)data.Width * data.Height * 4 * 4 / 3; // + ~1/3 more for GenerateMipmap's chain
            SetParameters(hdr: false);
        }

        TrackCreated();
    }

    // Decides whether a decoded LDR image should be block-compressed and, if so, does the actual
    // BC1/BC3 encoding right here — pure CPU work, no GL calls at all. Deliberately *not* done
    // inside the constructor above (where the first version of texture compression put it): that
    // meant real per-texture CPU cost (a nearest-palette search over every pixel of every mip
    // level) landing serially on the GL thread during ResourceSystem's "GL upload" phase, which
    // used to be cheap (TexImage2D + GenerateMipmap are fast, GPU-side). The fix is to call this
    // from wherever Decode/DecodeWithOpacity themselves run — ResourceSystem.DecodeTextureKey,
    // inside the same Task.Run worker as decode — so compression rides the same
    // already-parallel-across-textures decode phase instead of serializing after it.
    public static void CompressIfEligible(TextureData data, bool allowCompression)
    {
        if (data.IsHdr || !allowCompression) return;
        if (data.Width < MinCompressibleSize || data.Height < MinCompressibleSize) return;
        if (_s3tcSupported is not true) return; // WarmCompressionSupport wasn't called, or the driver doesn't have it

        data.CompressedHasAlpha = BlockCompression.HasAlpha(data.Ldr);
        data.CompressedLevels   = BlockCompression.Compress(data.Ldr!, data.Width, data.Height, data.CompressedHasAlpha);
    }

    // Must run once on the GL thread, with a live context, before any texture decode starts —
    // CompressIfEligible needs the answer but has no GL context of its own to ask (it runs on
    // ResourceSystem's background decode workers). Called from Engine.InitializeOpenGL, right
    // after the context itself is created.
    public static void WarmCompressionSupport(GL gl) => _s3tcSupported ??= gl.IsExtensionPresent("GL_EXT_texture_compression_s3tc");

    public GLTexture(GL gl, string path) : this(gl, Decode(path)) { }

    public static TextureData Decode(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (HDRLoader.IsHDRPath(fullPath))
        {
            var img = HDRLoader.Load(fullPath);
            return new TextureData { IsHdr = true, Width = img.Width, Height = img.Height, Hdr = img.Pixels };
        }

        using var image = Image.Load<Rgba32>(fullPath);
        image.Mutate(x => x.Flip(FlipMode.Vertical));

        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        PremultiplyAlpha(pixels);

        return new TextureData { IsHdr = false, Width = image.Width, Height = image.Height, Ldr = pixels };
    }

    // Merges a standalone greyscale opacity map into the albedo's alpha channel at load time —
    // most PBR/foliage texture packs ship opacity as its own file, and the cutout test
    // (shaderPBR.frag, ZPrepass, ShadowMapper) reads alpha straight off the albedo texture, so
    // this is where that gets baked in instead of requiring an offline compositing step.
    public static TextureData DecodeWithOpacity(string albedoPath, string opacityPath)
    {
        using var albedo  = Image.Load<Rgba32>(Path.GetFullPath(albedoPath));
        using var opacity = Image.Load<Rgba32>(Path.GetFullPath(opacityPath));

        if (opacity.Size != albedo.Size)
            opacity.Mutate(x => x.Resize(albedo.Size));

        albedo.Mutate(x => x.Flip(FlipMode.Vertical));
        opacity.Mutate(x => x.Flip(FlipMode.Vertical));

        var pixels = new byte[albedo.Width * albedo.Height * 4];
        albedo.CopyPixelDataTo(pixels);

        var opacityPixels = new byte[opacity.Width * opacity.Height * 4];
        opacity.CopyPixelDataTo(opacityPixels);

        for (var i = 0; i < pixels.Length; i += 4)
            pixels[i + 3] = opacityPixels[i];   // opacity map's red channel -> albedo's alpha

        PremultiplyAlpha(pixels);

        return new TextureData { IsHdr = false, Width = albedo.Width, Height = albedo.Height, Ldr = pixels };
    }

    public unsafe GLTexture(GL gl, Span<byte> data, uint width, uint height) : base(gl)
    {
        Handle = Gl.GenTexture();
        Gl.BindTexture(TextureTarget.Texture2D, Handle);

        fixed (void* d = &data[0])
        {
            Gl.TexImage2D(
                TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, d);
        }

        ApproxBytes = (long)width * height * 4;
        SetParameters(hdr: false);
        TrackCreated();
    }

    // Uploads an already-compressed mip chain (CompressIfEligible did the actual encoding, off
    // the GL thread) level by level, since GPU-side glGenerateMipmap generally can't operate on
    // compressed internal formats (they aren't framebuffer/colour-renderable, which mipmap
    // generation relies on) — this is also why the caller passes skipGenerateMipmap: true to
    // SetParameters afterward.
    private unsafe void UploadCompressed(List<BlockCompression.Level> levels, bool hasAlpha)
    {
        var format = hasAlpha ? InternalFormat.CompressedRgbaS3TCDxt5Ext : InternalFormat.CompressedRgbS3TCDxt1Ext;

        long totalBytes = 0;
        for (var level = 0; level < levels.Count; level++)
        {
            var lvl = levels[level];
            fixed (byte* p = lvl.Data)
                Gl.CompressedTexImage2D(
                    TextureTarget.Texture2D, level, format,
                    (uint)lvl.Width, (uint)lvl.Height, 0,
                    (uint)lvl.Data.Length, p);
            totalBytes += lvl.Data.Length;
        }

        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, levels.Count - 1);

        IsCompressed = true;
        ApproxBytes  = totalBytes;
    }

    private void TrackCreated()
    {
        TotalApproxBytes += ApproxBytes;
        if (IsHdr) return;
        if (IsCompressed) CompressedTextureCount++;
        else UncompressedTextureCount++;
    }

    // Mipmap generation and bilinear/anisotropic filtering blend neighboring texels' RGB
    // independent of alpha (not alpha-weighted). For an alpha-tested cutout texture (foliage),
    // the "transparent" texels surrounding a leaf shape often carry leftover matte/background
    // color from however the source image was authored/exported, and that color bleeds into
    // the leaf's edge at every mip level and any grazing-angle sample — the classic colored
    // fringe around alpha-tested foliage. Premultiplying means that bleed is toward black (0
    // RGB at 0 alpha) instead of an arbitrary color; shaderPBR.frag un-premultiplies on read so
    // surviving, alpha-tested pixels still get their real, correct color.
    private static void PremultiplyAlpha(byte[] rgba)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var a = rgba[i + 3];
            if (a == 255) continue;   // fully opaque — no change, and skips the common case fast

            rgba[i]     = (byte)(rgba[i]     * a / 255);
            rgba[i + 1] = (byte)(rgba[i + 1] * a / 255);
            rgba[i + 2] = (byte)(rgba[i + 2] * a / 255);
        }
    }

    // skipGenerateMipmap: true for a texture whose mip chain was already uploaded level-by-level
    // (UploadCompressed) — calling GenerateMipmap on top of that would try to overwrite it (and
    // likely silently no-op or fail outright on a compressed internal format regardless).
    private void SetParameters(bool hdr, bool skipGenerateMipmap = false)
    {
        GLSampler.Set(Gl, TextureTarget.Texture2D,
            wrapS: GLEnum.Repeat,
            wrapT: hdr ? GLEnum.ClampToEdge : GLEnum.Repeat,
            minFilter: hdr ? GLEnum.Linear : GLEnum.LinearMipmapLinear,
            magFilter: GLEnum.Linear
        );

        if (!hdr)
        {
            if (!skipGenerateMipmap)
                Gl.GenerateMipmap(TextureTarget.Texture2D);

            Gl.GetFloat(MaxSupportedAniso, out float maxAniso);
            _maxAniso = Math.Max(1f, Math.Min(AnisoRequest, maxAniso));   // 1 = off, if unsupported

            Gl.TexParameter(TextureTarget.Texture2D, TextureMaxAniso, _anisoEnabled ? _maxAniso : 1f);
            Anisotropic.Add(this);
        }
    }

    public static void SetAnisotropy(GL gl, bool enabled)
    {
        if (enabled == _anisoEnabled) return;
        _anisoEnabled = enabled;

        foreach (var tex in Anisotropic)
        {
            gl.BindTexture(TextureTarget.Texture2D, tex.Handle);
            gl.TexParameter(TextureTarget.Texture2D, TextureMaxAniso, enabled ? tex._maxAniso : 1f);
        }
    }

    protected override void DeleteGL()
    {
        Anisotropic.Remove(this);
        Gl.DeleteTexture(Handle);

        TotalApproxBytes -= ApproxBytes;
        if (IsHdr) return;
        if (IsCompressed) CompressedTextureCount--;
        else UncompressedTextureCount--;
    }
}
