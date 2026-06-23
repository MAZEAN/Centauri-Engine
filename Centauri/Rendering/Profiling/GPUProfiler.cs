namespace Centauri.Rendering.Profiling;

using Silk.NET.OpenGL;

public readonly record struct GpuTiming(string Name, double Milliseconds);

// Per-pass GPU timing via GL_TIME_ELAPSED queries, double-buffered so result reads never
// stall: each frame issues queries into one set while reading last frame's other set, and
// only reads a result once the driver reports it available. GL_TIME_ELAPSED can't nest, so
// zones must be sequential — Begin/End pairs with no overlap (overlapping calls are ignored).
public sealed class GpuProfiler : IDisposable
{
    private const int Sets     = 2;
    private const int MaxZones = 16;

    private readonly GL _gl;
    private readonly uint[,] _queries = new uint[Sets, MaxZones];
    private readonly bool[,] _issued  = new bool[Sets, MaxZones];
    private readonly string[] _names  = new string[MaxZones];
    private readonly double[] _ms     = new double[MaxZones];
    private readonly Dictionary<string, int> _slots = new();

    private int  _write;
    private int  _zoneCount;
    private bool _enabled;
    private bool _open;

    private readonly List<GpuTiming> _results = new();
    public IReadOnlyList<GpuTiming> Results => _results;

    public GpuProfiler(GL gl)
    {
        _gl = gl;
        for (var s = 0; s < Sets; s++)
            for (var z = 0; z < MaxZones; z++)
                _queries[s, z] = _gl.GenQuery();
    }

    // Read last frame's results (the set we're NOT about to write), then arm this frame.
    public void BeginFrame(bool enabled)
    {
        _enabled = enabled;
        _results.Clear();
        if (!enabled) return;

        _write ^= 1;
        var read = _write ^ 1;

        for (var z = 0; z < _zoneCount; z++)
        {
            if (!_issued[read, z]) continue;

            _gl.GetQueryObject(_queries[read, z], QueryObjectParameterName.QueryResultAvailable, out uint ready);
            if (ready == 0) continue;   // not done — keep last value rather than stalling

            _gl.GetQueryObject(_queries[read, z], QueryObjectParameterName.QueryResult, out uint ns);
            _ms[z] = ns / 1_000_000.0;
        }

        for (var z = 0; z < _zoneCount; z++)
            _issued[_write, z] = false;

        for (var z = 0; z < _zoneCount; z++)
            _results.Add(new GpuTiming(_names[z], _ms[z]));
    }

    public Scope Measure(string name) => new(this, name);

    private void Begin(string name)
    {
        if (!_enabled || _open) return;   // ignore overlap — TIME_ELAPSED is singular

        var slot = Slot(name);
        _open = true;
        _issued[_write, slot] = true;
        _gl.BeginQuery(QueryTarget.TimeElapsed, _queries[_write, slot]);
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
        private readonly GpuProfiler _p;
        public Scope(GpuProfiler p, string name) { _p = p; p.Begin(name); }
        public void Dispose() => _p.End();
    }
}
