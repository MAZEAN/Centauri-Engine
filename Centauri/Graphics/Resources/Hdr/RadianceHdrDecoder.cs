namespace Centauri.Graphics.Resources.Hdr;

using System.Text;

// Minimal decoder for Radiance RGBE (.hdr / .pic) panoramas. Returns linear
// floating-point RGB — no tonemapping, no clamping — so the full dynamic
// range reaches the GPU. Handles the new-style adaptive RLE used by virtually
// every modern export, plus flat (uncompressed) and old-style RLE scanlines.

public readonly record struct HdrImage(float[] Pixels, int Width, int Height);

public static class RadianceHdrDecoder
{
    public static HdrImage Decode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var pos = 0;

        // --- magic ----------------------------------------------------------
        var magic = ReadLine(bytes, ref pos);
        if (!magic.StartsWith("#?"))
            throw new InvalidDataException($"'{path}' is not a Radiance HDR file.");

        // --- header ---------------------------------------------------------
        var formatSeen = false;
        while (true)
        {
            var line = ReadLine(bytes, ref pos);
            if (line.Length == 0) break;                       // blank line ends the header
            if (!line.StartsWith("FORMAT=")) continue;

            if (!line.Contains("32-bit_rle_rgbe") && !line.Contains("32-bit_rle_xyze"))
                throw new InvalidDataException($"Unsupported HDR format: '{line}'.");
            formatSeen = true;
        }
        if (!formatSeen)
            throw new InvalidDataException("HDR header missing FORMAT line.");

        // --- resolution -----------------------------------------------------
        // Only the standard "-Y H +X W" orientation is supported; that covers
        // every equirectangular panorama you'd use as a skybox.
        var res = ReadLine(bytes, ref pos);
        var parts = res.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "-Y" || parts[2] != "+X")
            throw new InvalidDataException($"Unsupported HDR resolution line: '{res}'.");

        var height = int.Parse(parts[1]);
        var width  = int.Parse(parts[3]);

        var rgb      = new float[width * height * 3];
        var scanline = new byte[width * 4];

        for (var y = 0; y < height; y++)
        {
            ReadScanline(bytes, ref pos, scanline, width);

            // flip vertically — the file is top-to-bottom, the texture bottom-up
            var row = (height - 1 - y) * width * 3;
            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                RgbeToFloat(scanline[i], scanline[i + 1], scanline[i + 2], scanline[i + 3],
                            rgb, row + x * 3);
            }
        }

        return new HdrImage(rgb, width, height);
    }

    private static void ReadScanline(byte[] src, ref int pos, byte[] dst, int width)
    {
        // New-style adaptive RLE is only valid for these widths; anything else
        // is necessarily flat or old-style.
        if (width is < 8 or > 0x7fff)
        {
            ReadFlatScanline(src, ref pos, dst, width);
            return;
        }

        // A new-style scanline begins with the marker 0x02 0x02 followed by the
        // 16-bit width. Absent that, fall back to the flat reader.
        if (src[pos] != 2 || src[pos + 1] != 2 || (src[pos + 2] & 0x80) != 0)
        {
            ReadFlatScanline(src, ref pos, dst, width);
            return;
        }

        if (((src[pos + 2] << 8) | src[pos + 3]) != width)
            throw new InvalidDataException("HDR scanline width mismatch.");

        pos += 4;

        // Four channel planes (R, G, B, E), each RLE-encoded independently.
        for (var ch = 0; ch < 4; ch++)
        {
            var x = 0;
            while (x < width)
            {
                var count = src[pos++];
                if (count > 128)
                {
                    // run: (count - 128) copies of the next byte
                    var value = src[pos++];
                    var run   = count - 128;
                    while (run-- > 0) dst[x++ * 4 + ch] = value;
                }
                else
                {
                    // literal: `count` raw bytes
                    while (count-- > 0) dst[x++ * 4 + ch] = src[pos++];
                }
            }
        }
    }

    // Flat scanlines, including the legacy (1,1,1,n) repeat-run encoding.
    private static void ReadFlatScanline(byte[] src, ref int pos, byte[] dst, int width)
    {
        var x = 0;
        var shift = 0;
        byte pr = 0, pg = 0, pb = 0, pe = 0;

        while (x < width)
        {
            var r = src[pos++];
            var g = src[pos++];
            var b = src[pos++];
            var e = src[pos++];

            if (r == 1 && g == 1 && b == 1)
            {
                // old-style run: repeat the previous pixel (e << shift) times
                var run = e << shift;
                while (run-- > 0 && x < width)
                {
                    var o = x++ * 4;
                    dst[o] = pr; dst[o + 1] = pg; dst[o + 2] = pb; dst[o + 3] = pe;
                }
                shift += 8;
            }
            else
            {
                var o = x++ * 4;
                dst[o] = r; dst[o + 1] = g; dst[o + 2] = b; dst[o + 3] = e;
                pr = r; pg = g; pb = b; pe = e;
                shift = 0;
            }
        }
    }

    // RGBE -> linear float. The shared exponent is a power-of-two scale; an
    // exponent of zero encodes pure black.
    private static void RgbeToFloat(byte r, byte g, byte b, byte e, float[] dst, int o)
    {
        if (e == 0)
        {
            dst[o] = dst[o + 1] = dst[o + 2] = 0f;
            return;
        }

        var f = (float)Math.ScaleB(1.0, e - (128 + 8));   // ldexp(1, e - 136)
        dst[o]     = r * f;
        dst[o + 1] = g * f;
        dst[o + 2] = b * f;
    }

    private static string ReadLine(byte[] src, ref int pos)
    {
        var start = pos;
        while (pos < src.Length && src[pos] != (byte)'\n') pos++;

        var len = pos - start;
        if (len > 0 && src[start + len - 1] == (byte)'\r') len--;   // tolerate CRLF

        var line = Encoding.ASCII.GetString(src, start, len);
        if (pos < src.Length) pos++;                                // consume '\n'
        return line;
    }
}