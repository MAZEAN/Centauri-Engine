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
    private readonly EnvironmentLoader _environmentLoader;
    private readonly CommandHistory _commandHistory;

    private IKeyboard _keyboard = null!;
    public IInputContext InputContext { get; private set; } = null!;

    private Vector2 _mousePos;

    private readonly Dictionary<Camera, CameraController> _controllers = new();
    private readonly DebugHotkeys _hotkeys;

    public InputSystem(IWindow window, Scene scene, AppConfig config, RenderingSystem renderingSystem, EntitySetLoader entitySetLoader, EnvironmentLoader environmentLoader, CommandHistory commandHistory)
    {
        _window          = window;
        _scene           = scene;
        _config          = config;
        _renderingSystem = renderingSystem;
        _entitySetLoader = entitySetLoader;
        _environmentLoader = environmentLoader;
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
        var picked = _scene.Pick(ray);

        // Ctrl+click in the viewport toggles, same as the Outliner's Ctrl+click — no Shift-range-
        // select here though, there's no natural "list order" to range over in the 3D view the way
        // there is in the Outliner's rows. Ctrl+click on empty space (picked is null) is a no-op —
        // it leaves whatever's already selected alone, rather than clearing it, matching every
        // other app's "Ctrl+click misses everything" behavior.
        var ctrl = _keyboard.IsKeyPressed(Key.ControlLeft) || _keyboard.IsKeyPressed(Key.ControlRight);

        if (ctrl)
        {
            if (picked is not null) _scene.ToggleSelect(picked);
        }
        else
        {
            _scene.Select(picked);
        }
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

        if (key == Key.Delete && _config.Input.Mode == ViewMode.Edit && _scene.SelectedEntities.Count > 0)
        {
            // Snapshotted first — deleting mutates Scene.SelectedEntities (RemoveEntity drops the
            // deleted entity from it), so iterating the live list while deleting from it would skip
            // entries.
            var toDelete = _scene.SelectedEntities.ToList();
            var commands = new List<ICommand>(toDelete.Count);

            foreach (var entity in toDelete)
            {
                // Captured *before* deleting — DeleteEntity untracks the entity, which is exactly
                // the bookkeeping Capture needs to still be intact.
                if (_entitySetLoader.Capture(entity) is { } captured)
                    commands.Add(new DeleteEntityCommand(_entitySetLoader, entity, captured.Definition, captured.SourcePath));

                _entitySetLoader.DeleteEntity(entity);
            }

            // One CompositeCommand (via PushRange) for the whole selection, not one Ctrl+Z per
            // entity — same reasoning as the gizmo's multi-select drag (TransformGizmo.EndDrag).
            _commandHistory.PushRange(commands);
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
            _environmentLoader.Save();
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