namespace Centauri.Graphics.Resources;

using System.Buffers.Binary;

// Software BC1 (DXT1, opaque) / BC3 (DXT5, alpha) block compression for LDR textures — used by
// GLTexture's compressed-upload path when the driver reports GL_EXT_texture_compression_s3tc. A
// from-scratch encoder rather than a native library, matching this project's existing aversion to
// per-RID native binaries (see the ImGuizmo rationale in Docs/Documentation/Gizmos.md §1) — BC1/BC3
// are simple enough (fixed 4x4 block, bounding-box endpoints, nearest-palette-color indices) that a
// plain C# encoder is a few hundred lines, not worth a dependency for. Endpoint selection is a
// per-channel bounding box, not the principal-component analysis real encoders (stb_dxt, etc.) use
// — a "good enough baseline," not best-in-class quality; see Docs/Documentation/TextureCompression.md.
internal static class BlockCompression
{
    private const int Bc1BlockBytes = 8;
    private const int Bc3BlockBytes = 16;

    public readonly record struct Level(int Width, int Height, byte[] Data);

    // Compresses the full mip chain (box-filter downsampled, one BC1/BC3 level per step) down to
    // 1x1 — the same "how many levels" a GPU-generated chain would have
    // (floor(log2(max(w,h)))+1). Edge-clamped sampling (Sample below) means every level, including
    // ones whose dimensions aren't multiples of 4 (the norm once the chain gets small, and possibly
    // true of level 0 too for a non-power-of-two source image), still encodes correctly — no
    // separate "only compress if divisible by 4" restriction needed.
    public static List<Level> Compress(ReadOnlySpan<byte> rgba, int width, int height, bool hasAlpha)
    {
        var levels = new List<Level>();
        var current = rgba.ToArray();
        var w = width;
        var h = height;

        while (true)
        {
            var size = hasAlpha ? Bc3Size(w, h) : Bc1Size(w, h);
            var data = new byte[size];
            if (hasAlpha) EncodeBc3(current, w, h, data);
            else EncodeBc1(current, w, h, data);
            levels.Add(new Level(w, h, data));

            if (w == 1 && h == 1) break;

            current = Downsample(current, w, h, out var nw, out var nh);
            w = nw;
            h = nh;
        }

        return levels;
    }

    // True if any pixel's alpha is below fully opaque — decides BC1 (no alpha channel at all,
    // ~6:1 vs. RGBA8) vs. BC3 (~4:1 vs. RGBA8, but round-trips alpha).
    public static bool HasAlpha(ReadOnlySpan<byte> rgba)
    {
        for (var i = 3; i < rgba.Length; i += 4)
            if (rgba[i] != 255)
                return true;
        return false;
    }

    private static int BlocksX(int w) => (w + 3) / 4;
    private static int BlocksY(int h) => (h + 3) / 4;
    private static int Bc1Size(int w, int h) => BlocksX(w) * BlocksY(h) * Bc1BlockBytes;
    private static int Bc3Size(int w, int h) => BlocksX(w) * BlocksY(h) * Bc3BlockBytes;

    private static void EncodeBc1(ReadOnlySpan<byte> rgba, int width, int height, Span<byte> dst)
    {
        var bx = BlocksX(width);
        var by = BlocksY(height);
        var o = 0;
        for (var y = 0; y < by; y++)
        for (var x = 0; x < bx; x++)
        {
            EncodeColorBlock(rgba, width, height, x * 4, y * 4, dst.Slice(o, Bc1BlockBytes), forceFourColorMode: true);
            o += Bc1BlockBytes;
        }
    }

    private static void EncodeBc3(ReadOnlySpan<byte> rgba, int width, int height, Span<byte> dst)
    {
        var bx = BlocksX(width);
        var by = BlocksY(height);
        var o = 0;
        for (var y = 0; y < by; y++)
        for (var x = 0; x < bx; x++)
        {
            EncodeAlphaBlock(rgba, width, height, x * 4, y * 4, dst.Slice(o, 8));
            EncodeColorBlock(rgba, width, height, x * 4, y * 4, dst.Slice(o + 8, 8), forceFourColorMode: false);
            o += Bc3BlockBytes;
        }
    }

    // Clamps to the image's own edge — covers both a partial block at the image's right/bottom
    // border and every ragged (non-multiple-of-4) mip level near the bottom of the chain.
    private static (byte R, byte G, byte B, byte A) Sample(ReadOnlySpan<byte> rgba, int width, int height, int x, int y)
    {
        x = Math.Min(x, width - 1);
        y = Math.Min(y, height - 1);
        var i = (y * width + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }

    // BC1's colour block: two RGB565 endpoints + 16 x 2-bit indices into a 4-colour palette
    // (endpoints plus two interpolated thirds). Reused as-is for BC3's colour half — BC3 always
    // decodes its colour block in 4-colour mode regardless of endpoint order (unlike standalone
    // BC1, where colour0 <= colour1 as a raw uint16 switches decoders into a 3-colour +
    // transparent-black mode) so forceFourColorMode only matters for the BC1 caller.
    private static void EncodeColorBlock(ReadOnlySpan<byte> rgba, int width, int height, int bx, int by, Span<byte> dst, bool forceFourColorMode)
    {
        Span<byte> r = stackalloc byte[16];
        Span<byte> g = stackalloc byte[16];
        Span<byte> b = stackalloc byte[16];
        byte minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;

        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
        {
            var (pr, pg, pb, _) = Sample(rgba, width, height, bx + x, by + y);
            var i = y * 4 + x;
            r[i] = pr; g[i] = pg; b[i] = pb;
            if (pr < minR) minR = pr; if (pr > maxR) maxR = pr;
            if (pg < minG) minG = pg; if (pg > maxG) maxG = pg;
            if (pb < minB) minB = pb; if (pb > maxB) maxB = pb;
        }

        var c0 = Pack565(maxR, maxG, maxB);
        var c1 = Pack565(minR, minG, minB);

        if (forceFourColorMode && c0 <= c1)
            c0 = c1 == ushort.MaxValue ? (ushort)(c1 - 1) : (ushort)(c1 + 1); // nudge apart to keep the 4-colour (opaque) decode mode

        var (r0, g0, b0) = Unpack565(c0);
        var (r1, g1, b1) = Unpack565(c1);

        // Explicit stackalloc (not a `Span<int> x = [a, b, c, d]` collection expression) — called
        // once per 4x4 block, tens of thousands of times per texture, so this needs to provably
        // never heap-allocate rather than trust the compiler's lowering of a collection expression
        // with non-constant elements.
        Span<int> palR = stackalloc int[4] { r0, r1, (2 * r0 + r1) / 3, (r0 + 2 * r1) / 3 };
        Span<int> palG = stackalloc int[4] { g0, g1, (2 * g0 + g1) / 3, (g0 + 2 * g1) / 3 };
        Span<int> palB = stackalloc int[4] { b0, b1, (2 * b0 + b1) / 3, (b0 + 2 * b1) / 3 };

        uint indices = 0;
        for (var i = 0; i < 16; i++)
        {
            var best = 0;
            var bestDist = int.MaxValue;
            for (var p = 0; p < 4; p++)
            {
                var dr = r[i] - palR[p];
                var dg = g[i] - palG[p];
                var db = b[i] - palB[p];
                var dist = dr * dr + dg * dg + db * db;
                if (dist < bestDist) { bestDist = dist; best = p; }
            }
            indices |= (uint)best << (i * 2);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(dst, c0);
        BinaryPrimitives.WriteUInt16LittleEndian(dst[2..], c1);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[4..], indices);
    }

    // BC3's alpha block: two 8-bit endpoints + 16 x 3-bit indices into an 8-value palette.
    // alpha0 = block max, alpha1 = block min, so whenever the block isn't perfectly flat this
    // lands in the higher-precision 8-value interpolation mode (alpha0 > alpha1) rather than the
    // 6-value + hard 0/255 mode reserved for alpha0 <= alpha1 — we have no punch-through-alpha
    // need to reserve exact 0/255 for.
    private static void EncodeAlphaBlock(ReadOnlySpan<byte> rgba, int width, int height, int bx, int by, Span<byte> dst)
    {
        Span<byte> a = stackalloc byte[16];
        byte min = 255, max = 0;

        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
        {
            var (_, _, _, av) = Sample(rgba, width, height, bx + x, by + y);
            a[y * 4 + x] = av;
            if (av < min) min = av;
            if (av > max) max = av;
        }

        byte a0 = max, a1 = min;
        Span<int> pal = stackalloc int[8];
        pal[0] = a0;
        pal[1] = a1;

        if (a0 > a1)
        {
            pal[2] = (6 * a0 + 1 * a1) / 7;
            pal[3] = (5 * a0 + 2 * a1) / 7;
            pal[4] = (4 * a0 + 3 * a1) / 7;
            pal[5] = (3 * a0 + 4 * a1) / 7;
            pal[6] = (2 * a0 + 5 * a1) / 7;
            pal[7] = (1 * a0 + 6 * a1) / 7;
        }
        else
        {
            pal[2] = (4 * a0 + 1 * a1) / 5;
            pal[3] = (3 * a0 + 2 * a1) / 5;
            pal[4] = (2 * a0 + 3 * a1) / 5;
            pal[5] = (1 * a0 + 4 * a1) / 5;
            pal[6] = 0;
            pal[7] = 255;
        }

        ulong indices = 0;
        for (var i = 0; i < 16; i++)
        {
            var best = 0;
            var bestDist = int.MaxValue;
            for (var p = 0; p < 8; p++)
            {
                var d = a[i] - pal[p];
                var dist = d * d;
                if (dist < bestDist) { bestDist = dist; best = p; }
            }
            indices |= (ulong)best << (i * 3);
        }

        dst[0] = a0;
        dst[1] = a1;
        dst[2] = (byte)indices;
        dst[3] = (byte)(indices >> 8);
        dst[4] = (byte)(indices >> 16);
        dst[5] = (byte)(indices >> 24);
        dst[6] = (byte)(indices >> 32);
        dst[7] = (byte)(indices >> 40);
    }

    // 2x2 box filter, edge-clamped the same way the block encoders are — handles odd source
    // dimensions (the last covered column/row just re-samples the edge pixel) without a separate
    // padding case.
    private static byte[] Downsample(ReadOnlySpan<byte> rgba, int width, int height, out int newWidth, out int newHeight)
    {
        newWidth  = Math.Max(1, width  / 2);
        newHeight = Math.Max(1, height / 2);
        var dst = new byte[newWidth * newHeight * 4];

        for (var y = 0; y < newHeight; y++)
        for (var x = 0; x < newWidth; x++)
        {
            var (r0, g0, b0, a0) = Sample(rgba, width, height, x * 2,     y * 2);
            var (r1, g1, b1, a1) = Sample(rgba, width, height, x * 2 + 1, y * 2);
            var (r2, g2, b2, a2) = Sample(rgba, width, height, x * 2,     y * 2 + 1);
            var (r3, g3, b3, a3) = Sample(rgba, width, height, x * 2 + 1, y * 2 + 1);

            var i = (y * newWidth + x) * 4;
            dst[i]     = (byte)((r0 + r1 + r2 + r3 + 2) / 4);
            dst[i + 1] = (byte)((g0 + g1 + g2 + g3 + 2) / 4);
            dst[i + 2] = (byte)((b0 + b1 + b2 + b3 + 2) / 4);
            dst[i + 3] = (byte)((a0 + a1 + a2 + a3 + 2) / 4);
        }

        return dst;
    }

    private static ushort Pack565(byte r, byte g, byte b) =>
        (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

    // Expands with high-bit replication (r5<<3 | r5>>2, etc.), the same rule GPU decoders use to
    // fill 565's missing low bits — needed so the encoder's own nearest-colour search is judging
    // distance against the same 888 endpoint colours hardware will actually reconstruct.
    private static (int R, int G, int B) Unpack565(ushort c)
    {
        var r5 = (c >> 11) & 0x1F;
        var g6 = (c >> 5)  & 0x3F;
        var b5 = c & 0x1F;
        return ((r5 << 3) | (r5 >> 2), (g6 << 2) | (g6 >> 4), (b5 << 3) | (b5 >> 2));
    }
}
