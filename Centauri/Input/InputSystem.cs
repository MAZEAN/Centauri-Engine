namespace Centauri.Input;

using Silk.NET.Input;
using Silk.NET.Windowing;
using System.Linq;
using System.Numerics;

using Config;
using World;
using Rendering;
using Loading;
using Editing.Undo;

public class InputSystem : IDisposable
{
    private readonly IWindow _window;
    private readonly Scene _scene;
    private readonly AppConfig _config;
    private readonly RenderingSystem _renderingSystem;
    private readonly EntitySetLoader _entitySetLoader;
    private readonly CommandHistory _commandHistory;

    private IKeyboard _keyboard = null!;
    public IInputContext InputContext { get; private set; } = null!;

    private Vector2 _mousePos;

    private readonly Dictionary<Camera, CameraController> _controllers = new();
    private readonly DebugHotkeys _hotkeys;

    public InputSystem(IWindow window, Scene scene, AppConfig config, RenderingSystem renderingSystem, EntitySetLoader entitySetLoader, CommandHistory commandHistory)
    {
        _window          = window;
        _scene           = scene;
        _config          = config;
        _renderingSystem = renderingSystem;
        _entitySetLoader = entitySetLoader;
        _commandHistory  = commandHistory;

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
        if (_config.Input.Mode == ViewMode.Edit && button == MouseButton.Left && !_renderingSystem.ImGuiWantsMouse)
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

        var ctrl = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);

        if (key == Key.S && ctrl)
        {
            SaveScene();
            return;
        }

        // Shift-guarded (not just Ctrl+R) — this discards every live, unsaved edit, so it
        // shouldn't be one accidental keystroke away from Ctrl+S.
        if (key == Key.R && ctrl && (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight)))
        {
            ResetScene();
            return;
        }

        // Undo/redo — same raw-keyboard pattern as Ctrl+S/Ctrl+Shift+R above, so (like those) this
        // doesn't check whether an ImGui text field has focus and would rather handle Ctrl+Z itself
        // (a pre-existing gap, not new to this).
        if (key == Key.Z && ctrl)
        {
            _commandHistory.Undo();
            return;
        }

        if (key == Key.Y && ctrl)
        {
            _commandHistory.Redo();
            return;
        }

        if (key == _config.Input.ToggleModeKey)
        {
            ToggleMode(); _scene.ClearSelection();
            return;
        }

        if (key == Key.Delete && _config.Input.Mode == ViewMode.Edit && _scene.Selected is { } selected)
        {
            // Captured *before* deleting — DeleteEntity untracks the entity, which is exactly the
            // bookkeeping Capture needs to still be intact.
            if (_entitySetLoader.Capture(selected) is { } captured)
                _commandHistory.Push(new DeleteEntityCommand(_entitySetLoader, selected, captured.Definition, captured.SourcePath));

            _entitySetLoader.DeleteEntity(selected);
            return;
        }

        if (_renderingSystem.ImGuiWantsKeyboard) return;

        _hotkeys.Handle(key);
    }

    private void SaveScene()
    {
        try
        {
            _entitySetLoader.Save();
            Console.WriteLine("[Scene] Saved.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Scene] Save failed: {ex.Message}");
        }
    }

    private void ResetScene()
    {
        try
        {
            _entitySetLoader.Reset();

            // Every command on the stack potentially references an Entity/Transform Reset just
            // disposed — see CommandHistory.Clear's own comment.
            _commandHistory.Clear();

            Console.WriteLine("[Scene] Reset to last saved state.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Scene] Reset failed: {ex.Message}");
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