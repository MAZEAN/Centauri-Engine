namespace SimpleTerrain.Input;

using Silk.NET.Input;
using Silk.NET.Windowing;
using System.Linq;
using System.Numerics;

using Config;
using World;

public class InputSystem
{
    private readonly IWindow _window;
    private readonly Scene _scene;
    private readonly AppConfig _config;
    
    private readonly Dictionary<Camera, CameraController> _controllers = new();
    private IKeyboard _keyboard= null!;
    
    public InputSystem(IWindow window, Scene scene, AppConfig config)
    {
        _window = window;
        _scene = scene;
        _config = config;
    }


    public void Initialize()
    {
        var input = _window.CreateInput();

        _keyboard = input.Keyboards.FirstOrDefault()
                    ?? throw new InvalidOperationException("Keyboard not available");

        _keyboard.KeyDown += OnKeyDown;

        foreach (var mouse in input.Mice)
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
            case Key.Escape:
                _window.Close();
                break;

            case Key.Number1:
                SwitchCamera("Main");
                break;

            case Key.Number2:
                SwitchCamera("Debug");
                break;

            case Key.Tab:
                _scene.CycleCamera();
                ResetActiveController();
                break;
            
            case Key.F1:
            case Key.F2:
            case Key.F3:
            case Key.F4:
            case Key.F5:
                HandleDebugToggle(key);
                break;
        }
    }
    
    private void HandleDebugToggle(Key key)
    {
        switch (key)
        {
            case Key.F1: _scene.Settings.ToggleShowDebugView();     break;
            case Key.F2: _scene.Settings.ToggleShowBoundingBoxes(); break;
            case Key.F3: _scene.Settings.ToggleShowFrustums();      break;
            case Key.F4: _scene.Settings.ToggleShowCameras();       break;
            case Key.F5: _scene.Settings.ToggleEnableCulling();     break;
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