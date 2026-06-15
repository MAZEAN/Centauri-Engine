namespace Centauri.Graphics.Resources.Hdr;


// Loads high-dynamic-range panoramas straight to linear float RGB, skipping
// the lossy "convert to PNG first" step. Radiance <c>.hdr</c> is decoded
// in-house; OpenEXR <c>.exr</c> is decoded through the pure-managed
// TinyEXR.NET library (all compressions supported).

public static class HdrLoader
{
    /// <summary>True when the path carries an extension this loader handles.</summary>
    public static bool IsHdrPath(string path)
        => HasExt(path, ".hdr") || HasExt(path, ".exr");

    public static HdrImage Load(string path)
    {
        if (HasExt(path, ".hdr")) return RadianceHdrDecoder.Decode(path);
        if (HasExt(path, ".exr")) return LoadExr(path);
        throw new NotSupportedException($"'{path}' is not a supported HDR image.");
    }

    private static HdrImage LoadExr(string path)
    {
        // TinyEXR returns interleaved RGBA, top-to-bottom. We strip alpha and
        // flip vertically to land in the engine's bottom-up RGB layout.
        var result = TinyEXR.Exr.LoadEXR(path, out float[] rgba, out int width, out int height);
        if (result != TinyEXR.ResultCode.Success)
            throw new InvalidDataException($"Failed to decode EXR '{path}': {result}.");

        var rgb = new float[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var srcRow = y * width * 4;
            var dstRow = (height - 1 - y) * width * 3;
            for (var x = 0; x < width; x++)
            {
                rgb[dstRow + x * 3 + 0] = rgba[srcRow + x * 4 + 0];
                rgb[dstRow + x * 3 + 1] = rgba[srcRow + x * 4 + 1];
                rgb[dstRow + x * 3 + 2] = rgba[srcRow + x * 4 + 2];
            }
        }

        return new HdrImage(rgb, width, height);
    }

    private static bool HasExt(string path, string ext)
        => Path.GetExtension(path).Equals(ext, StringComparison.OrdinalIgnoreCase);
}