namespace Centauri.World.Collections;

using System.Collections;
using Silk.NET.Windowing;

public sealed class CameraRig : IEnumerable<Camera>
{
    private readonly List<Camera> _cameras = new();
    private readonly Dictionary<string, Camera> _byName = new();
    private Camera? _active, _primary;

    public IReadOnlyList<Camera> All => _cameras;
    public int Count => _cameras.Count;

    public Camera Active  => _active  ?? throw new Exception("No active camera set.");
    public Camera Primary => _primary ?? _active ?? throw new Exception("No primary camera set.");

    public void Add(Camera cam)
    {
        if (!_byName.TryAdd(cam.Name, cam))
            throw new Exception($"Camera with name '{cam.Name}' already exists.");

        _cameras.Add(cam);
        _active ??= cam;
    }

    public void SetActive(string name)  => _active  = Get(name);
    public void SetPrimary(string name) => _primary = Get(name);

    public void Cycle()
    {
        if (_cameras.Count == 0) throw new Exception("No cameras available.");
        var i = _active is null ? 0 : (_cameras.IndexOf(_active) + 1) % _cameras.Count;
        _active = _cameras[i];
    }

    public void InitializeAspect(IWindow window)
    {
        foreach (var cam in _cameras)
            cam.SetAspectRatio(window.FramebufferSize);
    }

    private Camera Get(string name) =>
        _byName.TryGetValue(name, out var cam) ? cam : throw new Exception($"Camera '{name}' not found.");

    public IEnumerator<Camera> GetEnumerator() => _cameras.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}