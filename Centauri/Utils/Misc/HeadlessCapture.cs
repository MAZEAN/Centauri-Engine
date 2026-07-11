namespace Centauri.Utils.Misc;

using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// Opt-in headless smoke-test support: set CENTAURI_HEADLESS_FRAMES to auto-close the window and
// dump a screenshot after that many rendered frames, instead of running interactively forever.
// Lets a build/CI environment (e.g. Xvfb + Mesa llvmpipe, no physical GPU) verify the engine
// actually renders, not just compiles. No effect on normal runs unless the env var is set.
public static class HeadlessCapture
{
    public static int? FrameLimit { get; } = ParseFrameLimit();

    public static string ScreenshotPath =>
        Environment.GetEnvironmentVariable("CENTAURI_SCREENSHOT_PATH") ?? "screenshot.png";

    private static int? ParseFrameLimit()
    {
        var raw = Environment.GetEnvironmentVariable("CENTAURI_HEADLESS_FRAMES");
        return int.TryParse(raw, out var n) && n > 0 ? n : null;
    }

    public static unsafe void SaveFramebuffer(GL gl, int width, int height, string path)
    {
        var pixels = new byte[width * height * 4];
        fixed (byte* p = pixels)
            gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        using var image = Image.LoadPixelData<Rgba32>(pixels, width, height);
        image.Mutate(ctx => ctx.Flip(FlipMode.Vertical));   // GL's row order is bottom-up
        image.SaveAsPng(path);
    }
}
