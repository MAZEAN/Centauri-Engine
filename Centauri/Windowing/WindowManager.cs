namespace Centauri.Windowing;

using Silk.NET.Windowing;
using Silk.NET.Core.Contexts;
using Config;

public class WindowManager
{
    public static IWindow CreateWindow(AppConfig config, IWindowCallbacks callbacks)
    {
        var options = CreateWindowOptions(config);

        var window = Window.Create(options);
        
        window.Load              += callbacks.OnLoad;
        window.Update            += callbacks.OnUpdate;
        window.Render            += callbacks.OnRender;
        window.FramebufferResize += callbacks.OnResize;
        window.Closing           += callbacks.OnClose;

        return window;
    }
    
    // GL 4.3 core, forward-compatible (no legacy fixed-function fallback — matches the engine's
    // existing core-profile-only usage). Every current shader still declares "#version 330 core"
    // and keeps working unmodified under this context (GL is backward compatible within core
    // profiles) — this bump is the "Step 1" context-only change from
    // Docs/Documentation/GL4Upgrade.md; adopting 4.3-only GLSL features (compute shaders, SSBOs,
    // etc.) happens per-subsystem afterward, not as part of this change.
    private static readonly APIVersion TargetGLVersion = new(4, 3);

    private static WindowOptions CreateWindowOptions(AppConfig config)
    {
        var options = WindowOptions.Default;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible,
            TargetGLVersion);

        var monitor = FindMonitor();
        options.WindowState = config.Window.State;
        options.WindowBorder = config.Window.Border;
        options.Position = monitor.Bounds.Origin;
        options.Title = config.Window.Title;
        options.VSync = config.Window.EnableVSync;
        return options;
    }

    private static IMonitor FindMonitor()
    {
        var monitor = Monitor.GetMonitors(null)
            .OrderByDescending(m =>
            {
                var r = m.VideoMode.Resolution;
                return r.HasValue ? r.Value.X * r.Value.Y : 0;
            })
            .First();
        return monitor;
    }
}