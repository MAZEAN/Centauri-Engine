namespace Centauri.Rendering.UI;

using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImGuiNET;
using System.Numerics;

using Utils.Misc;
using Config;

public class ImGuiManager : IDisposable
{
    private readonly IWindow _window;
    private readonly ImGuiController _controller;
    public ImFontPtr Font { get; private set; }

    public ImGuiManager(GL gl, ImGuiConfig config, IWindow window, IInputContext input)
    {
        _window = window;
        
        ImFontPtr font = default;
        
        _controller = new ImGuiController(gl, window, input, onConfigureIO: () =>
        {
            var io = ImGui.GetIO();

            io.Fonts.AddFontDefault();
            
            font = io.Fonts.AddFontFromFileTTF(
                PathResolver.Resolve(config.Font), config.FontSize);
        });

        Font = font;
    }

    public void Update(float deltaTime)
    {
        _controller.Update(deltaTime);
        
        var io = ImGui.GetIO();
        io.DisplayFramebufferScale = new Vector2(
            (float)_window.FramebufferSize.X / Math.Max(1, _window.Size.X),
            (float)_window.FramebufferSize.Y / Math.Max(1, _window.Size.Y));
    }
    public void Render() => _controller.Render();

    public void Dispose() => _controller.Dispose();
}