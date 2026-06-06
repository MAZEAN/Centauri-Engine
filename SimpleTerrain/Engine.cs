namespace SimpleTerrain;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Maths;

using Config;
using World;
using Rendering.Systems;
using Input;

public class Engine
{
    private IWindow _window = null!;
    private GL _gl = null!;
    private AppConfig _config = null!;
    private InputSystem _input = null!;
    private Scene _scene = null!;
    private RenderingSystem _renderingSystem = null!;
    private ResourceSystem _resourceSystem = null!;
    private SceneLoader _sceneLoader = null!;
    
    private int _frameCount;
    private double _fpsTimer;

    public void Run()
    {
        var configPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..",
                "Config", "config.json"));

        _config = ConfigLoader.Load(configPath);
        _scene  = new Scene(_config);

        var options = CreateWindowOptions();
        _window = Window.Create(options);

        _window.Load              += OnLoad;
        _window.Update            += OnUpdate;
        _window.Render            += OnRender;
        _window.FramebufferResize += OnResize;
        _window.Closing           += OnClose;

        _window.Run();
        _window.Dispose();
    }

    private WindowOptions CreateWindowOptions()
    {
        var options = WindowOptions.Default;
        
        var monitor = Monitor.GetMonitors(null)
            .OrderByDescending(m =>
            {
                var r = m.VideoMode.Resolution;
                return r.HasValue ? r.Value.X * r.Value.Y : 0;
            })
            .First();

        options.WindowState = _config.Window.WindowState;
        options.Position = monitor.Bounds.Origin;
        options.Title = _config.Window.Title;
        options.VSync = _config.Window.EnableVSync;
        options.Samples = _config.Window.Samples;
        return options;
    }

    private void OnLoad()
    {
        try
        {
            InitializeOpenGL();

            _renderingSystem = new RenderingSystem(_gl, _config);

            _resourceSystem = new ResourceSystem(_gl, _config);

            _sceneLoader = new SceneLoader(_resourceSystem, _scene, _config);
            _sceneLoader.Load();
            
            foreach (var cam in _scene.Cameras)
                cam.SetAspectRatio(_window.FramebufferSize);
            
            _input    = new InputSystem(_window, _scene, _config);
            _input.Initialize();
        }
        catch (Exception e)
        {
            Console.WriteLine($"OnLoad failed: {e}");
            throw;
        }
    }

    private void InitializeOpenGL()
    {
        _gl = GL.GetApi(_window);

        // background color when clearing the frame
        var c = _config.Window.ClearColor;
        _gl.ClearColor(c[0], c[1], c[2], c[3]);

        // discard fragments that are behind already-drawn geometry
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);

        // smooth jagged edges — sample count set in WindowConfig.Samples
        _gl.Enable(GLEnum.Multisample);

        // define the render region — important for high-DPI displays
        _gl.Viewport(_window.FramebufferSize);

        // enable alpha transparency
        _gl.Enable(EnableCap.Blend);

        // blend color and alpha channels separately:
        // color: srcAlpha * src + (1 - srcAlpha) * dst  — standard transparency
        // alpha: 1 * src + 0 * dst                      — preserve source alpha
        _gl.BlendFuncSeparate(
            BlendingFactor.SrcAlpha,
            BlendingFactor.OneMinusSrcAlpha,
            BlendingFactor.One,
            BlendingFactor.Zero
        );

        // skip rendering triangles facing away from camera — halves fragment work
        // requires consistent counter-clockwise winding in exported meshes (Blender default)
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.Ccw);

        // needed if you add skybox or reflections later — removes seams on cubemap edges
        _gl.Enable(EnableCap.TextureCubeMapSeamless);

        // default fill mode — change to PolygonMode.Line for wireframe debugging
        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
    }

    private void SetFPSCounter(float deltaTime)
    {
        _fpsTimer   += deltaTime;
        _frameCount += 1;

        if (_fpsTimer >= 1.0)
        {
            var fps = _frameCount / _fpsTimer;
            var ms  = 1000.0 / fps;

            _window.Title = $"{_config.Window.Title} — {fps:F1} FPS ({ms:F2} ms)";

            _frameCount = 0;
            _fpsTimer   = 0;
        }
    }

    private void OnUpdate(double deltaTime)
    {
        var delta = (float)deltaTime;
        _input.Update(delta);
        SetFPSCounter(delta);
    }

    private void OnRender(double deltaTime)
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        
        _renderingSystem.Render(_scene, deltaTime);
    }
    
    private void OnResize(Vector2D<int> size)
    {
        _gl.Viewport(size);
        
        foreach (var cam in _scene.Cameras)
            cam.SetAspectRatio(size);
    }
    
    private void OnClose()
    {
        _scene.Dispose();
        _resourceSystem.Dispose();
        _renderingSystem.Dispose();
    }
}