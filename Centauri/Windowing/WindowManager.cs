namespace Centauri.Windowing;

using Silk.NET.Windowing;
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
    
    private static WindowOptions CreateWindowOptions(AppConfig config)
    {
        var options = WindowOptions.Default;

        var monitor = FindMonitor();
        
        options.WindowState = config.Window.WindowState;
        options.Position = monitor.Bounds.Origin;
        options.Title = config.Window.Title;
        options.VSync = config.Window.EnableVSync;
        options.Samples = config.Window.Samples;
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