namespace SimpleTerrain.Rendering.Systems;

using Silk.NET.OpenGL;
using Config;
using Renderers;
using World;

public class RenderingSystem : IDisposable
{
    private GL _gl = null!;
    private readonly AppConfig _config = null!;
    private readonly Renderer _renderer = null!;
    private readonly GridRenderer _gridRenderer = null!;
    private readonly CameraRenderer _cameraRenderer = null!;

    public RenderingSystem(GL gl, AppConfig config)
    {
        _gl = gl;
        _config = config;
        
        _renderer = new Renderer(gl, config);
        _gridRenderer = new GridRenderer(gl, config.Window);
        _cameraRenderer = new CameraRenderer(gl);
    }

    public void Render(Scene scene, double deltaTime)
    {
        _gridRenderer.Render(scene);
        _renderer.Render(scene, (float) deltaTime);
        _cameraRenderer.Render(scene);
    }

    public void Dispose()
    {
        _gridRenderer.Dispose();
        _cameraRenderer.Dispose();
    }
}