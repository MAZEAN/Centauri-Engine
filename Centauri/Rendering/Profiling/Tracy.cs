namespace Centauri.Rendering.Profiling;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

// Thin P/Invoke wrapper over the Tracy profiler's plain-C client API (ThirdParty/tracy —
// see Docs/TracyProfiler.md for what it is and how to build the native library it binds to).
// Every entry point here is safe to call unconditionally: if the native library isn't present,
// or Enabled is off, calls are no-ops instead of throwing. Nothing about this class depends on
// whether Tracy is actually installed, so instrumentation call sites never need their own guard.
//
// CPU zones map onto Tracy's own Begin/End zone model (Scope, mirroring GPUProfiler.Measure's
// Scope). GPU-side numbers instead ride Tracy's plot API (see GPUProfiler.BeginFrame) rather
// than Tracy's native GPU-zone protocol: real GPU zones need timestamp-based begin/end queries
// calibrated against the CPU clock, a different (and pricier to get subtly wrong) model than
// the GL_TIME_ELAPSED durations GPUProfiler already computes. Plots show the exact same
// per-pass millisecond numbers as live graphs instead of a stacked in-frame timeline — less
// visually integrated, but a direct, low-risk reuse of measurements that already exist.
public static class Tracy
{
    private const string Lib = "TracyClient";

    public static bool Enabled { get; set; }
    public static bool IsAvailable { get; }
    
    public static string? LoadError { get; }


    private static readonly Dictionary<string, ulong> SrcLocs = new();

    static Tracy()
    {
        try
        {
            NativeLibrary.Load(Lib, typeof(Tracy).Assembly, DllImportSearchPath.SafeDirectories);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            LoadError = ex.Message;
        }    }

    // A CPU zone, active from Scope() to Dispose(). No-ops (default, inert) when Tracy isn't
    // available or isn't enabled, so `using var _ = Tracy.Scope("X");` is always safe to leave in.
    public static Zone Scope(string name,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = "")
    {
        if (!IsAvailable || !Enabled) return default;

        var srcloc = GetSrcLoc(name, file, line, member);
        return new Zone(___tracy_emit_zone_begin_alloc(srcloc, 1));
    }

    // A named, live numeric graph in Tracy's timeline — used for the GPU pass timings
    // GPUProfiler already measures (see its BeginFrame).
    public static void Plot(string name, double value)
    {
        if (!IsAvailable || !Enabled) return;
        ___tracy_emit_plot(name, value);
    }

    // Marks the end of one frame so Tracy's timeline shows frame boundaries. Call once per
    // frame, after everything else for that frame has been recorded.
    public static void FrameMark()
    {
        if (!IsAvailable || !Enabled) return;
        ___tracy_emit_frame_mark(IntPtr.Zero);
    }

    public static bool Connected => IsAvailable && ___tracy_connected() != 0;

    // Tracy identifies each zone by a cached "source location" handle; ___tracy_alloc_srcloc_name
    // duplicates the strings internally, so the handle (not the strings) is what needs caching —
    // keyed by the zone's display name, since that's what's actually shown in Tracy's UI.
    private static ulong GetSrcLoc(string name, string file, int line, string member)
    {
        if (SrcLocs.TryGetValue(name, out var cached)) return cached;

        var srcloc = ___tracy_alloc_srcloc_name(
            (uint)line,
            file,   (nuint)Encoding.UTF8.GetByteCount(file),
            member, (nuint)Encoding.UTF8.GetByteCount(member),
            name,   (nuint)Encoding.UTF8.GetByteCount(name),
            0);

        SrcLocs[name] = srcloc;
        return srcloc;
    }

    public readonly ref struct Zone
    {
        private readonly ZoneCtx _ctx;
        private readonly bool _active;

        internal Zone(ZoneCtx ctx) { _ctx = ctx; _active = true; }

        public void Dispose()
        {
            if (_active) ___tracy_emit_zone_end(_ctx);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ZoneCtx
    {
        public uint Id;
        public int  Active;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong ___tracy_alloc_srcloc_name(
        uint line,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string source, nuint sourceSz,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string function, nuint functionSz,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nuint nameSz,
        uint color);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern ZoneCtx ___tracy_emit_zone_begin_alloc(ulong srcloc, int active);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ___tracy_emit_zone_end(ZoneCtx ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ___tracy_emit_frame_mark(IntPtr name);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ___tracy_emit_plot(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, double val);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ___tracy_connected();
}
