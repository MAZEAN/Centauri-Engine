namespace SimpleTerrain.Rendering.Systems;

using Silk.NET.OpenGL;
using Config;
using Renderers;
using World;

public class RenderingSystem : IDisposable
{
    private readonly GL             _gl;
    private readonly AppConfig      _config;
    private readonly Renderer       _renderer;
    private readonly GridRenderer   _gridRenderer;
    private readonly DebugRenderer  _debugRenderer;

    public RenderingSystem(GL gl, AppConfig config)
    {
        _gl            = gl;
        _config        = config;
        _renderer      = new Renderer(gl, config);
        _gridRenderer  = new GridRenderer(gl, config.Window);
        _debugRenderer = new DebugRenderer(gl);
    }

    public void Render(Scene scene, double deltaTime)
    {
        _gridRenderer.Render(scene);
        _renderer.Render(scene, (float)deltaTime);

        if (scene.DebugSettings.ShowDebugView)
        {
            var active         = scene.GetActiveCamera();
            var cullingCamera  = scene.GetPrimaryCamera();

            _debugRenderer.Begin(active);

            _debugRenderer.DrawCameras(scene);
            _debugRenderer.DrawAllAABBs(scene, cullingCamera.Frustum);

            _debugRenderer.End();
        }
    }

    public void Dispose()
    {
        _gridRenderer.Dispose();
        _debugRenderer.Dispose();
    }
}