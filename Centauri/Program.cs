namespace Centauri;

class Program
{
    static void Main(string[] args)
    {
        // TEMPORARY diagnostic (CENTAURI_GLFW_DIAG=1) — Silk.NET.Windowing.Glfw's own
        // GlfwPlatform.IsApplicable swallows the real exception from loading the native GLFW
        // library and only prints it via #if DEBUG in *its own* precompiled package build
        // (Release, like virtually every published NuGet package) — so building *our* project
        // in Debug has no effect on that and tells us nothing. This probes the same native-load
        // call directly, in our own code, so the real exception (missing native asset vs. an
        // unresolved transitive symbol vs. something else) actually reaches the log. Remove once
        // the real CI headless-boot failure is diagnosed and fixed.
        if (Environment.GetEnvironmentVariable("CENTAURI_GLFW_DIAG") == "1")
        {
            try
            {
                using var glfw = Silk.NET.GLFW.Glfw.GetApi();
                Console.WriteLine("[GlfwDiag] Silk.NET.GLFW.Glfw.GetApi() succeeded.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GlfwDiag] Silk.NET.GLFW.Glfw.GetApi() failed: {ex}");
            }
        }

        var engine = new Engine();
        engine.Run();
    }
}
