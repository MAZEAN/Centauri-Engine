namespace Centauri.Input;

using Silk.NET.Input;
using System.Numerics;

using World;
using Config;

public class CameraController
{
    private readonly Camera _camera;
    private readonly CameraConfig _config;

    private Vector2 _lastMouse;
    private bool _seeded;

    private const float MaxDelta = 400f;

    public CameraController(Camera camera, CameraConfig config)
    {
        _camera = camera;
        _config = config;
    }

    public void UpdateMovement(IKeyboard keyboard, float deltaTime)
    {
        var moveSpeed = _config.MoveSpeed * deltaTime;

        if (keyboard.IsKeyPressed(Key.ShiftLeft))
            moveSpeed *= 2.0f;

        if (keyboard.IsKeyPressed(Key.W))           _camera.UpdatePosition( _camera.Forward * moveSpeed);
        if (keyboard.IsKeyPressed(Key.S))           _camera.UpdatePosition(-_camera.Forward * moveSpeed);
        if (keyboard.IsKeyPressed(Key.A))           _camera.UpdatePosition(-_camera.Right   * moveSpeed);
        if (keyboard.IsKeyPressed(Key.D))           _camera.UpdatePosition( _camera.Right   * moveSpeed);
        if (keyboard.IsKeyPressed(Key.Space))       _camera.UpdatePosition( _camera.Up      * moveSpeed);
        if (keyboard.IsKeyPressed(Key.ControlLeft)) _camera.UpdatePosition(-_camera.Up      * moveSpeed);
    }

    // call when a drag begins so the first move doesn't apply a huge jump
    public void BeginDrag() => _seeded = false;

    public void Look(Vector2 position)
    {
        if (TryDelta(position, out var d))
            _camera.ModifyDirection(d.X * _config.SensitivityX, d.Y * _config.SensitivityY);
    }

    public void Zoom(ScrollWheel scroll)
        => _camera.AdjustZoom(-scroll.Y * _config.ZoomSensitivity);

    private bool TryDelta(Vector2 position, out Vector2 delta)
    {
        if (!_seeded)
        {
            _lastMouse = position;
            _seeded = true;
            delta = Vector2.Zero;
            return false;
        }

        delta = position - _lastMouse;
        _lastMouse = position;

        if (!(delta.LengthSquared() > MaxDelta * MaxDelta)) return true;
        
        delta = Vector2.Zero;
        return false;
    }

    public void Reset() => BeginDrag();
}