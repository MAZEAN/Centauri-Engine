namespace SimpleTerrain.Input;

using Silk.NET.Input;
using System.Numerics;
using World;
using Config;

public class CameraController
{
    private Vector2 _lastMousePosition;
    private readonly Camera _camera;
    private readonly CameraConfig _config;
    
    private bool _initialized = false;
    
    public CameraController(Camera camera, CameraConfig config)
    {
        _camera = camera;
        _config = config;
    }
    
    public void UpdateMovement(IKeyboard keyboard, float deltaTime)
    {
        float moveSpeed = _config.MoveSpeed * deltaTime;

        if (keyboard.IsKeyPressed(Key.W))
            _camera.UpdatePosition(_camera.Forward * moveSpeed);

        if (keyboard.IsKeyPressed(Key.S))
            _camera.UpdatePosition(-_camera.Forward * moveSpeed);

        if (keyboard.IsKeyPressed(Key.A))
            _camera.UpdatePosition(-_camera.Right * moveSpeed);

        if (keyboard.IsKeyPressed(Key.D))
            _camera.UpdatePosition(_camera.Right * moveSpeed);

        if (keyboard.IsKeyPressed(Key.Space))
            _camera.UpdatePosition(_camera.Up * moveSpeed);

        if (keyboard.IsKeyPressed(Key.ControlLeft))
            _camera.UpdatePosition(-_camera.Up * moveSpeed);
    }
    
    public void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (!_initialized)
        {
            _lastMousePosition = position;
            _initialized = true;
            return;
        }

        var xOffset = (position.X - _lastMousePosition.X) * _config.SensitivityX;
        var yOffset = (position.Y - _lastMousePosition.Y) * _config.SensitivityY;

        _lastMousePosition = position;

        _camera.ModifyDirection(xOffset, yOffset);
    }

    
    public void OnMouseWheel(IMouse mouse, ScrollWheel scrollWheel)
    {
        _camera.AdjustZoom(-scrollWheel.Y * _config.ZoomSensitivity);
    }
    
    public void Reset()
    {
        _initialized = false;
    }
}