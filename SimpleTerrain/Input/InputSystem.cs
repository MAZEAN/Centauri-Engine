namespace SimpleTerrain.Input;

using Silk.NET.Input;
using Silk.NET.Windowing;
using System.Linq;
using System.Numerics;

using Config;
using Scene;

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
    
    public void UpdateMovement(float deltaTime)
    {
        var moveSpeed = _config.MoveSpeed * deltaTime;
        var camera = _scene.GetActiveCamera();

        if (_keyboard.IsKeyPressed(Key.W))
        {
            //Move forwards
            camera.UpdatePosition(camera.Forward * moveSpeed);
        }
        if (_keyboard.IsKeyPressed(Key.S))
        {
            //Move backwards
            camera.UpdatePosition(camera.Forward * -moveSpeed);
        }
        if (_keyboard.IsKeyPressed(Key.A))
        {
            //Move left
            camera.UpdatePosition(Vector3.Normalize(Vector3.Cross(camera.Forward, camera.Up)) * -moveSpeed);
        }
        if (_keyboard.IsKeyPressed(Key.D))
        {
            //Move right
            camera.UpdatePosition(Vector3.Normalize(Vector3.Cross(camera.Forward, camera.Up)) * moveSpeed);
        }
        if (_keyboard.IsKeyPressed(Key.Space))
        {
            //Move up
            camera.UpdatePosition(camera.Up * moveSpeed);
        }
        if (_keyboard.IsKeyPressed(Key.ControlLeft))
        {
            //Move down
            camera.UpdatePosition(camera.Up * -moveSpeed);
        }
    }
    
    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        var cam = _scene.GetActiveCamera();
        if (!_controllers.TryGetValue(cam, out var controller))
        {
            controller = new CameraController(cam, _config.Camera);
            _controllers[cam] = controller;
        }

        controller.OnMouseMove(mouse, position);
    }
    
    private void OnMouseWheel(IMouse mouse, ScrollWheel scroll)
    {
        var cam = _scene.GetActiveCamera();
        if (!_controllers.TryGetValue(cam, out var controller))
        {
            controller = new CameraController(cam, _config.Camera);
            _controllers[cam] = controller;
        }

        controller.OnMouseWheel(mouse, scroll);
    }
    
    private void OnKeyDown(IKeyboard keyboard, Key key, int code)
    {
        if (key == Key.Escape)
            _window.Close();

        if (key == Key.F1)
            SwitchCamera("Main");

        if (key == Key.F2)
            SwitchCamera("Debug");

        if (key == Key.Tab)
        {
            _scene.CycleCamera();
            ResetActiveController();
        }
        
        if (key == Key.C)
            _scene.ToggleEnableCulling();
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