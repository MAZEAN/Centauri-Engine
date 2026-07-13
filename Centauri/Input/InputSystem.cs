namespace Centauri.Input;

using Silk.NET.Input;
using Silk.NET.Windowing;
using System.Linq;
using System.Numerics;

using Config;
using World;
using Rendering;
using Loading;

public class InputSystem : IDisposable
{
    private readonly IWindow _window;
    private readonly Scene _scene;
    private readonly AppConfig _config;
    private readonly RenderingSystem _renderingSystem;
    private readonly SceneLoader _sceneLoader;

    private IKeyboard _keyboard = null!;
    public IInputContext InputContext { get; private set; } = null!;

    private Vector2 _mousePos;

    private readonly Dictionary<Camera, CameraController> _controllers = new();
    private readonly DebugHotkeys _hotkeys;

    public InputSystem(IWindow window, Scene scene, AppConfig config, RenderingSystem renderingSystem, SceneLoader sceneLoader)
    {
        _window          = window;
        _scene           = scene;
        _config          = config;
        _renderingSystem = renderingSystem;
        _sceneLoader     = sceneLoader;

        _hotkeys         = new DebugHotkeys(config, scene, ResetActiveController);

        Initialize();
    }

    public void Initialize()
    {
        InputContext = _window.CreateInput();
        _keyboard = InputContext.Keyboards.FirstOrDefault()
                    ?? throw new InvalidOperationException("Keyboard not available");

        _keyboard.KeyDown += OnKeyDown;

        foreach (var mouse in InputContext.Mice)
        {
            mouse.Cursor.CursorMode = CursorMode.Raw; // start in Fly mode

            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.Scroll    += OnMouseWheel;
        }
    }

    public void Update(float deltaTime)
    {
        if (_config.Input.Mode != ViewMode.Fly) return;
        GetController(_scene.Cameras.Active).UpdateMovement(_keyboard, deltaTime);
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        _mousePos = position;
        if (_config.Input.Mode != ViewMode.Fly) return;

        GetController(_scene.Cameras.Active).Look(position);
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (_config.Input.Mode == ViewMode.Edit
            && button == MouseButton.Left
            && !_renderingSystem.ImGuiWantsMouse)
        {
            PickAtCursor();
        }
    }

    private void OnMouseWheel(IMouse mouse, ScrollWheel scroll)
    {
        if (_config.Input.Mode == ViewMode.Edit && !_keyboard.IsKeyPressed(Key.ShiftLeft)) return;
        GetController(_scene.Cameras.Active).Zoom(scroll);
    }

    private void PickAtCursor()
    {
        var cam = _scene.Cameras.Active;
        var ray = cam.ScreenPointToRay(_mousePos, new Vector2(_window.Size.X, _window.Size.Y));
        _scene.Select(_scene.Pick(ray));
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int code)
    {
        if (key == Key.Escape)
        {
            _window.Close();
            return;
        }

        if (key == Key.S && (keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight)))
        {
            SaveScene();
            return;
        }

        if (key == _config.Input.ToggleModeKey)
        {
            ToggleMode(); _scene.ClearSelection();
            return;
        }

        if (_renderingSystem.ImGuiWantsKeyboard) return;
        
        _hotkeys.Handle(key);
    }

    private void SaveScene()
    {
        if (!_sceneLoader.CanSave)
        {
            Console.WriteLine("[Scene] Save skipped: scene uses \"include\" and can't be saved yet.");
            return;
        }

        try
        {
            _sceneLoader.Save();
            Console.WriteLine("[Scene] Saved.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Scene] Save failed: {ex.Message}");
        }
    }

    private void ToggleMode()
    {
        _config.Input.ToggleMode();

        SetCursor(_config.Input.Mode == ViewMode.Fly ? CursorMode.Raw : CursorMode.Normal);

        if (_config.Input.Mode == ViewMode.Fly) ResetActiveController();
    }

    private void SetCursor(CursorMode mode)
    {
        foreach (var mouse in InputContext.Mice)
            mouse.Cursor.CursorMode = mode;
    }

    private CameraController GetController(Camera cam)
    {
        if (_controllers.TryGetValue(cam, out var controller)) return controller;
        
        controller = new CameraController(cam, _config.Camera);
        _controllers[cam] = controller;
        return controller;
    }

    private void ResetActiveController()
        => GetController(_scene.Cameras.Active).BeginDrag();

    public void Dispose()
    {
        _keyboard.KeyDown -= OnKeyDown;
        foreach (var mouse in InputContext.Mice)
        {
            mouse.MouseMove -= OnMouseMove;
            mouse.MouseDown -= OnMouseDown;
            mouse.Scroll    -= OnMouseWheel;
        }
        InputContext.Dispose();
    }
}