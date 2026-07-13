namespace Centauri;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Maths;

using Config;
using Utils.Misc;
using World;
using Rendering;
using Input;
using Loading;
using Windowing;
using Simulation;

public class Engine : IWindowCallbacks
{
    private IWindow _window = null!;
    private GL _gl = null!;
    private AppConfig _config = null!;
    private InputSystem _inputSystem = null!;
    private Scene _scene = null!;
    private RenderingSystem _renderingSystem = null!;
    private ResourceSystem _resourceSystem = null!;
    private SceneLoader _sceneLoader = null!;
    private SimulationSystem _simulation = null!;
    private ShaderHotReload? _shaderHotReload;

    private int _frameCount;

    private const string ConfigPath = "Config/config.json";

    public void Run()
    {
        _config = ConfigLoader.Load(ConfigPath);
        _scene  = new Scene();

        using var window = WindowManager.CreateWindow(_config, this);

        _window = window;
        window.Run();
    }

    public void OnLoad()
    {
        try
        {
            using (Time.Measure("Startup total"))
            {
                Time.Run("OpenGL init", InitializeOpenGL);
                Time.Run("Systems",     InitializeSystems);
                Time.Run("Scene load",  LoadScene);
                Time.Run("Input",       InitializeInput);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OnLoad failed: {ex}");
            throw;
        }
    }

    private void InitializeSystems()
    {
        _resourceSystem  = new ResourceSystem(_gl, _config);
        _renderingSystem = new RenderingSystem(_gl, _config);
        _simulation = new SimulationSystem(_config);

        if (_config.Debug.ShaderHotReload)
            _shaderHotReload = new ShaderHotReload(PathResolver.Resolve("Shaders"));
    }

    private void LoadScene()
    {
        _sceneLoader = new SceneLoader(_resourceSystem, _scene, _config);
        Time.Run("Scene assets + entities", _sceneLoader.Load);
        
        _scene.Cameras.InitializeAspect(_window);
        Time.Run("IBL bake", () => _renderingSystem.BakeEnvironments(_scene));
    }

    private void InitializeInput()
    {
        _inputSystem = new InputSystem(_window, _scene, _config, _renderingSystem, _sceneLoader);

        _renderingSystem.InitializeComponents(_window, _inputSystem.InputContext);
        
        var fb = _window.FramebufferSize;
        _renderingSystem.Resize((uint)fb.X, (uint)fb.Y);
    }

    private void InitializeOpenGL()
    {
        _gl = GL.GetApi(_window);
        
        var c = _config.Window.ClearColor;
        _gl.ClearColor(c[0], c[1], c[2], c[3]);
        
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        
        _gl.Enable(GLEnum.Multisample);
        
        _gl.Viewport(_window.FramebufferSize);
        
        _gl.Enable(EnableCap.Blend);
        
        _gl.BlendFuncSeparate(
            BlendingFactor.SrcAlpha,
            BlendingFactor.OneMinusSrcAlpha,
            BlendingFactor.One,
            BlendingFactor.Zero
        );
        
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.Ccw);
        
        _gl.Enable(EnableCap.TextureCubeMapSeamless);
        
        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
    }

    public void OnUpdate(double deltaTime)
    {
        var delta = (float)deltaTime;

        _shaderHotReload?.Poll();
        _inputSystem.Update(delta);
        _simulation.Update(_scene, delta);
        _renderingSystem.Update(delta);
    }

    public void OnRender(double deltaTime)
    {
        _renderingSystem.Render(_scene, (float)deltaTime);

        if (HeadlessCapture.FrameLimit is { } limit && ++_frameCount >= limit)
        {
            var fb = _window.FramebufferSize;
            HeadlessCapture.SaveFramebuffer(_gl, fb.X, fb.Y, HeadlessCapture.ScreenshotPath);
            _window.Close();
        }
    }
    
    public void OnResize(Vector2D<int> size)
    {
        _gl.Viewport(size);
        
        _renderingSystem.Resize((uint)size.X, (uint)size.Y);
        
        foreach (var cam in _scene.Cameras)
            cam.SetAspectRatio(size);
    }
    
    public void OnClose()
    {
        _shaderHotReload?.Dispose();
        _renderingSystem.Dispose();
        _inputSystem.Dispose();
        _scene.Dispose();
        _resourceSystem.Dispose();
    }
}