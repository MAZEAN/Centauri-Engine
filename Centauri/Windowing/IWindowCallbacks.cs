namespace Centauri.Windowing;

using Silk.NET.Maths;

public interface IWindowCallbacks
{
    void OnLoad();
    void OnUpdate(double deltaTime);
    void OnRender(double deltaTime);
    void OnResize(Vector2D<int> size);
    void OnClose();
}