namespace Centauri.Graphics.Resources.HighDynamicRange;

// Loads high-dynamic-range panoramas straight to linear float RGB, skipping
// the lossy "convert to PNG first" step. Radiance <c>.hdr</c> is decoded
// in-house; OpenEXR <c>.exr</c> is decoded through the pure-managed
// TinyEXR.NET library (all compressions supported).

public static class HDRLoader
{
    // True when the path carries an extension this loader handles.
    public static bool IsHDRPath(string path)
        => HasExt(path, ".hdr") || HasExt(path, ".exr");

    public static HDRImage Load(string path)
    {
        var image =
            HasExt(path, ".hdr") ? RadianceHDRDecoder.Decode(path) :
            HasExt(path, ".exr") ? LoadExr(path) :
            throw new NotSupportedException($"'{path}' is not a supported HDR image.");
        
        ClampToHalfRange(image.Pixels);   // keep values finite so the RGB16F upload never yields +Inf
        return image;
    }

    private static HDRImage LoadExr(string path)
    {
        // TinyEXR returns interleaved RGBA, top-to-bottom. We strip alpha and
        // flip vertically to land in the engine's bottom-up RGB layout.
        var result = TinyEXR.Exr.LoadEXR(path, out var rgba, out var width, out var height);
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

        return new HDRImage(rgb, width, height);
    }
    
    private static void ClampToHalfRange(float[] pixels)
    {
        const float halfMax = 65504f;
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = Math.Clamp(pixels[i], 0f, halfMax);   // also drops stray negatives from EXR
    }

    private static bool HasExt(string path, string ext)
        => Path.GetExtension(path).Equals(ext, StringComparison.OrdinalIgnoreCase);
}