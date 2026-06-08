namespace Centauri.Input;

using Silk.NET.Input;
using Silk.NET.Windowing;
using System.Linq;
using System.Numerics;

using Config;
using World;
using Rendering.Systems;

public class InputSystem
{
    private readonly IWindow _window;
    private readonly Scene _scene;
    private readonly AppConfig _config;
    private readonly RenderingSystem _renderingSystem;
    
    private readonly Dictionary<Camera, CameraController> _controllers = new();
    private IKeyboard _keyboard= null!;
    
    private IInputContext _inputContext = null!;
    public IInputContext InputContext => _inputContext;
    
    public InputSystem(IWindow window, Scene scene, AppConfig config, RenderingSystem renderingSystem)
    {
        _window           = window;
        _scene            = scene;
        _config           = config;
        _renderingSystem  = renderingSystem;
    }
    
    public void Initialize()
    {
        _inputContext = _window.CreateInput();
        _keyboard     = _inputContext.Keyboards.FirstOrDefault()
                        ?? throw new InvalidOperationException("Keyboard not available");

        _keyboard.KeyDown += OnKeyDown;

        foreach (var mouse in _inputContext.Mice)
        {
            mouse.Cursor.CursorMode = CursorMode.Raw;

            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnMouseWheel;
        }
    }
    
    public void Update(float deltaTime)
    {
        var camera = _scene.GetActiveCamera();

        GetController(camera)
            .UpdateMovement(_keyboard, deltaTime);
    }
    
    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        GetController(_scene.GetActiveCamera())
            .OnMouseMove(mouse, position);
    }

    private void OnMouseWheel(IMouse mouse, ScrollWheel scroll)
    {
        GetController(_scene.GetActiveCamera())
            .OnMouseWheel(mouse, scroll);
    }
    
    private void OnKeyDown(IKeyboard keyboard, Key key, int code)
    {
        switch (key)
        {
            case Key.Escape: _window.Close(); break;
            case Key.M: _renderingSystem.ToggleStatsOverlay(); break;
            case Key.F6: _scene.DebugSettings.ToggleShowGrid(); break;
            case Key.Tab: _scene.CycleCamera(); ResetActiveController(); break;
            
            case Key.F1: _scene.DebugSettings.ToggleShowDebugView();     break;
            case Key.F2: _scene.DebugSettings.ToggleShowBoundingBoxes(); break;
            case Key.F3: _scene.DebugSettings.ToggleShowFrustums();      break;
            case Key.F4: _scene.DebugSettings.ToggleShowCameras();       break;
            case Key.F5: _scene.DebugSettings.ToggleEnableCulling();     break;
        }
    }
    
    private CameraController GetController(Camera cam)
    {
        if (!_controllers.TryGetValue(cam, out var controller))
        {
            controller = new CameraController(cam, _config.Camera);
            _controllers[cam] = controller;
        }

        return controller;
    }
    
    private void SwitchCamera(string name)
    {
        _scene.SetActiveCamera(name);
        ResetActiveController();
    }

    private void ResetActiveController()
    {
        var cam = _scene.GetActiveCamera();
        var controller = GetController(cam);

        controller.Reset();
    }
}