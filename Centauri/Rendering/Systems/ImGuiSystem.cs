namespace Centauri.Rendering.Systems;

using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using Silk.NET.Input;

public class ImGuiSystem : IDisposable
{
    private readonly ImGuiController _controller;

    public ImGuiSystem(GL gl, IWindow window, IInputContext input)
    {
        _controller = new ImGuiController(gl, window, input);
    }

    public void Update(float deltaTime) => _controller.Update(deltaTime);
    public void Render()                => _controller.Render();

    public void Dispose() => _controller.Dispose();
}