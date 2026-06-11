namespace Centauri;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Maths;

using Config;
using World;
using Rendering.Systems;
using Input;
using Loading;
using Windowing;

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

    public void Run()
    {
        _config = ConfigLoader.Load("Config/config.json");
        _scene  = new Scene();

        using var window = WindowManager.CreateWindow(_config, this);

        _window = window;
        window.Run();
    }

    public void OnLoad()
    {
        try
        {
            InitializeOpenGL();
            InitializeSystems();
            LoadScene();
            InitializeInput();
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
    }

    private void LoadScene()
    {
        _sceneLoader = new SceneLoader(_resourceSystem, _scene, _config);
        _sceneLoader.Load();

        _scene.InitializeCameras(_window);
    }

    private void InitializeInput()
    {
        _inputSystem = new InputSystem(_window, _scene, _config, _renderingSystem);

        _renderingSystem.InitializeImGui(_window, _inputSystem.InputContext);
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

    public void OnUpdate(double deltaTime)
    {
        var delta = (float)deltaTime;
        
        _inputSystem.Update(delta);
        _renderingSystem.Update(delta);
    }

    public void OnRender(double deltaTime)
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        
        _renderingSystem.Render(_scene, deltaTime);
    }
    
    public void OnResize(Vector2D<int> size)
    {
        _gl.Viewport(size);
        
        foreach (var cam in _scene.Cameras)
            cam.SetAspectRatio(size);
    }
    
    public void OnClose()
    {
        _renderingSystem.Dispose();
        _inputSystem.Dispose();
        _scene.Dispose();
        _resourceSystem.Dispose();
    }
}