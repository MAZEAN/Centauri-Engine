namespace Centauri.Utils.Misc;

using System.Diagnostics;
using System.Runtime.CompilerServices;

public static class Time
{
    private static readonly long Start = Stopwatch.GetTimestamp();

    public static float Now { get; private set; }

    public static void BeginFrame() => Now = (float)Stopwatch.GetElapsedTime(Start).TotalSeconds;
    
    public readonly struct Scope : IDisposable
    {
        private readonly string _label;
        private readonly string _caller;
        private readonly long   _start;

        public Scope(string label, string caller)
        {
            _label  = label;
            _caller = caller;
            _start  = Stopwatch.GetTimestamp();
        }

        public void Dispose() => Log(_caller, _label, _start);
    }

    // using-scope: logs the elapsed time when the block exits.
    public static Scope Measure(string label, [CallerFilePath] string file = "")
        => new(label, ClassName(file));

    public static void Run(string label, Action action, [CallerFilePath] string file = "")
    {
        var start = Stopwatch.GetTimestamp();
        action();
        
        Log(ClassName(file), label, start);
    }

    public static T Run<T>(string label, Func<T> func, [CallerFilePath] string file = "")
    {
        var start = Stopwatch.GetTimestamp();
        var result = func();
        
        Log(ClassName(file), label, start);
        return result;
    }

    private static void Log(string caller, string label, long start) =>
        Console.WriteLine($"[{caller}] {label}: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");

    private static string ClassName(string file) => Path.GetFileNameWithoutExtension(file);
}