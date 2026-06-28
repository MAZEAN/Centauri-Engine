namespace Centauri.Rendering.Profiling;

using Silk.NET.OpenGL;

public readonly record struct GpuTiming(string Name, double Milliseconds);

// Per-pass GPU timing via GL_TIME_ELAPSED queries, double-buffered so result reads never
// stall: each frame issues queries into one set while reading last frame's other set, and
// only reads a result once the driver reports it available. GL_TIME_ELAPSED can't nest, so
// zones must be sequential — Begin/End pairs with no overlap (overlapping calls are ignored).
public sealed class GPUProfiler : IDisposable
{
    private const int Sets     = 3;
    private const int MaxZones = 16;

    private readonly GL _gl;
    private readonly uint[,] _queries = new uint[Sets, MaxZones];
    private readonly bool[,] _issued  = new bool[Sets, MaxZones];
    private readonly string[] _names  = new string[MaxZones];
    private readonly double[] _ms     = new double[MaxZones];
    private readonly Dictionary<string, int> _slots = new();

    private int  _frame = Sets - 1;   // current write set; advanced each frame
    private int  _zoneCount;
    private bool _enabled;
    private bool _open;

    private readonly List<GpuTiming> _results = new();
    public IReadOnlyList<GpuTiming> Results => _results;

    public GPUProfiler(GL gl)
    {
        _gl = gl;
        for (var s = 0; s < Sets; s++)
            for (var z = 0; z < MaxZones; z++)
                _queries[s, z] = _gl.GenQuery();
    }

    // Advance to the set we'll write this frame — it was last used `Sets` frames ago, so its
    // results are long since ready (a 2-set/1-frame ring stalls or, with the availability
    // skip, freezes once the GPU runs a couple frames behind the CPU). Read it, then re-arm.
    public void BeginFrame(bool enabled)
    {
        _enabled = enabled;
        _results.Clear();
        if (!enabled) return;

        _frame = (_frame + 1) % Sets;

        for (var z = 0; z < _zoneCount; z++)
        {
            if (!_issued[_frame, z]) continue;

            _gl.GetQueryObject(_queries[_frame, z], QueryObjectParameterName.ResultAvailable, out uint ready);
            if (ready != 0)
            {
                _gl.GetQueryObject(_queries[_frame, z], QueryObjectParameterName.Result, out uint ns);
                _ms[z] = ns / 1_000_000.0;
            }

            _issued[_frame, z] = false;   // consumed; re-armed below if measured this frame
        }

        for (var z = 0; z < _zoneCount; z++)
            _results.Add(new GpuTiming(_names[z], _ms[z]));
    }

    public Scope Measure(string name) => new(this, name);

    private void Begin(string name)
    {
        if (!_enabled || _open) return;   // ignore overlap — TIME_ELAPSED is singular

        var slot = Slot(name);
        _open = true;
        _issued[_frame, slot] = true;
        _gl.BeginQuery(QueryTarget.TimeElapsed, _queries[_frame, slot]);
    }

    private void End()
    {
        if (!_enabled || !_open) return;
        _gl.EndQuery(QueryTarget.TimeElapsed);
        _open = false;
    }

    private int Slot(string name)
    {
        if (_slots.TryGetValue(name, out var slot)) return slot;

        slot = _zoneCount++;
        _slots[name] = slot;
        _names[slot] = name;
        return slot;
    }

    public void Dispose()
    {
        for (var s = 0; s < Sets; s++)
            for (var z = 0; z < MaxZones; z++)
                _gl.DeleteQuery(_queries[s, z]);
    }

    // using-scope around a pass: BeginQuery on enter, EndQuery on exit.
    public readonly ref struct Scope
    {
        private readonly GPUProfiler _p;
        public Scope(GPUProfiler p, string name) { _p = p; p.Begin(name); }
        public void Dispose() => _p.End();
    }
}
