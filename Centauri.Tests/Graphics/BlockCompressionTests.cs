namespace Centauri.Tests.Graphics;

using Centauri.Graphics.Resources;

// BlockCompression (BC1/BC3 software encoder) is pure C# — no GL context needed — unlike most of
// GLTexture's own upload path, so unlike the compressed-texture upload itself (verified only by
// headless boot + code review, see Docs/Documentation/TextureCompression.md), the encoder's actual
// bit-level output can be pinned directly. Decoding is done here with an independent, from-spec
// reimplementation of BC1/BC3 decode (not by calling back into BlockCompression's own private
// helpers) so a test failure means the *encoded bytes* are wrong, not just "whatever
// BlockCompression itself believes," which would be a vacuous round-trip.
public class BlockCompressionTests
{
    private static byte[] SolidColor(int width, int height, byte r, byte g, byte b, byte a)
    {
        var px = new byte[width * height * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = a;
        }
        return px;
    }

    [Fact]
    public void HasAlpha_DetectsAnyNonOpaquePixel()
    {
        var opaque = SolidColor(4, 4, 200, 100, 50, 255);
        Assert.False(BlockCompression.HasAlpha(opaque));

        var withHole = SolidColor(4, 4, 200, 100, 50, 255);
        withHole[^1] = 128; // last pixel's alpha byte
        Assert.True(BlockCompression.HasAlpha(withHole));
    }

    [Fact]
    public void Compress_OpaqueImage_UsesBc1BlockSize()
    {
        var rgba = SolidColor(4, 4, 10, 20, 30, 255);
        var levels = BlockCompression.Compress(rgba, 4, 4, hasAlpha: false);

        Assert.Equal(8, levels[0].Data.Length); // one BC1 block: 2 x uint16 endpoints + 4 bytes indices
    }

    [Fact]
    public void Compress_AlphaImage_UsesBc3BlockSize()
    {
        var rgba = SolidColor(4, 4, 10, 20, 30, 128);
        var levels = BlockCompression.Compress(rgba, 4, 4, hasAlpha: true);

        Assert.Equal(16, levels[0].Data.Length); // 8-byte alpha block + 8-byte BC1 colour block
    }

    [Fact]
    public void Compress_SolidBlock_DecodesBackToApproximatelyTheSameColor()
    {
        var rgba = SolidColor(4, 4, 200, 40, 90, 255);
        var levels = BlockCompression.Compress(rgba, 4, 4, hasAlpha: false);

        var (r, g, b) = DecodeBc1Pixel(levels[0].Data, pixelIndex: 5); // arbitrary interior pixel

        // 565 quantization loses precision (5/6/5 bits per channel) — a solid block should still
        // decode within a few 8-bit levels of the source, not just "some plausible colour."
        Assert.InRange(r, 200 - 8, 200 + 8);
        Assert.InRange(g, 40 - 4, 40 + 4);
        Assert.InRange(b, 90 - 8, 90 + 8);
    }

    [Fact]
    public void Compress_AlphaBlock_DecodesAlphaEndpointsExactly()
    {
        // A block whose 16 pixels alternate between two known alpha values — BC3 always picks
        // alpha0 = block max, alpha1 = block min as its two *stored* endpoints, so those two
        // exact values must survive untouched (only the interpolated in-between values are lossy).
        var rgba = new byte[4 * 4 * 4];
        for (var i = 0; i < 16; i++)
        {
            var a = i % 2 == 0 ? (byte)255 : (byte)0;
            rgba[i * 4 + 3] = a;
        }

        var levels = BlockCompression.Compress(rgba, 4, 4, hasAlpha: true);
        var data = levels[0].Data;

        Assert.Equal(255, data[0]); // alpha0 = max
        Assert.Equal(0,   data[1]); // alpha1 = min
    }

    [Theory]
    [InlineData(16, 8)]
    [InlineData(5, 3)]   // non-multiple-of-4, non-power-of-two — exercises the edge-clamped path
    [InlineData(1, 1)]
    public void Compress_MipChain_EndsAt1x1WithExpectedLevelCount(int width, int height)
    {
        var rgba = SolidColor(width, height, 1, 2, 3, 255);
        var levels = BlockCompression.Compress(rgba, width, height, hasAlpha: false);

        var expectedLevels = (int)Math.Floor(Math.Log2(Math.Max(width, height))) + 1;
        Assert.Equal(expectedLevels, levels.Count);

        Assert.Equal(width, levels[0].Width);
        Assert.Equal(height, levels[0].Height);
        Assert.Equal((1, 1), (levels[^1].Width, levels[^1].Height));

        // Every level's own byte size matches the ceil(w/4)*ceil(h/4)*8 BC1 formula — catches an
        // off-by-one in the block-count rounding for a ragged (non-multiple-of-4) level.
        foreach (var level in levels)
        {
            var expectedBytes = ((level.Width + 3) / 4) * ((level.Height + 3) / 4) * 8;
            Assert.Equal(expectedBytes, level.Data.Length);
        }
    }

    // Independent, from-spec BC1 decode of one pixel (index 0..15, row-major) — deliberately not
    // sharing any code with BlockCompression's own encoder.
    private static (int R, int G, int B) DecodeBc1Pixel(byte[] block, int pixelIndex)
    {
        var c0 = (ushort)(block[0] | (block[1] << 8));
        var c1 = (ushort)(block[2] | (block[3] << 8));
        var indices = (uint)(block[4] | (block[5] << 8) | (block[6] << 16) | (block[7] << 24));

        var (r0, g0, b0) = Expand565(c0);
        var (r1, g1, b1) = Expand565(c1);

        (int R, int G, int B)[] palette =
        [
            (r0, g0, b0),
            (r1, g1, b1),
            ((2 * r0 + r1) / 3, (2 * g0 + g1) / 3, (2 * b0 + b1) / 3),
            ((r0 + 2 * r1) / 3, (g0 + 2 * g1) / 3, (b0 + 2 * b1) / 3),
        ];

        var code = (int)((indices >> (pixelIndex * 2)) & 0b11);
        return palette[code];
    }

    private static (int R, int G, int B) Expand565(ushort c)
    {
        var r5 = (c >> 11) & 0x1F;
        var g6 = (c >> 5)  & 0x3F;
        var b5 = c & 0x1F;
        return ((r5 << 3) | (r5 >> 2), (g6 << 2) | (g6 >> 4), (b5 << 3) | (b5 >> 2));
    }
}
