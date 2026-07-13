namespace Centauri.Utils.Misc;

using Graphics.Resources;

// Opt-in dev convenience (see DebugConfig.ShaderHotReload): watches the Shaders/ tree for
// .vert/.frag/.comp edits and hot-swaps every live GLShader built from a changed file in place
// (GLShader.TryReload), instead of a full engine restart to see a shader tweak. Worth having
// specifically because this engine's iteration loop is shader-heavy (CSM/PCSS, the whole post
// stack) and startup is multi-second (asset decode, GL upload) — a restart-per-edit cycle is
// slow enough to actively discourage iterating on shaders at all.
public sealed class ShaderHotReload : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly HashSet<string> _pending = [];
    private readonly Lock _lock = new();

    public ShaderHotReload(string shadersRoot)
    {
        _watcher = new FileSystemWatcher(shadersRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite,
        };
        _watcher.Changed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    // FileSystemWatcher callbacks run on a thread-pool thread, never the GL thread — every GL
    // call has to happen from Poll() instead, so this only records *which* files changed.
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var ext = Path.GetExtension(e.FullPath);
        if (ext is not (".vert" or ".frag" or ".comp")) return;

        lock (_lock)
            _pending.Add(e.FullPath);
    }

    // Call once a frame from the GL thread. Draining into a local array before reloading (rather
    // than reloading while still holding the lock) keeps OnFileChanged from blocking on a slow
    // reload, and coalesces the editor's occasional double-fire per save into one attempt.
    public void Poll()
    {
        if (_pending.Count == 0) return;

        string[] changed;
        lock (_lock)
        {
            changed = [.. _pending];
            _pending.Clear();
        }

        foreach (var path in changed)
            ReloadShadersUsing(path);
    }

    private static void ReloadShadersUsing(string changedPath)
    {
        var full = Path.GetFullPath(changedPath);
        var name = Path.GetFileName(changedPath);

        foreach (var shader in GLShader.Live)
        {
            if (!Matches(shader.VertexPath, full) && !Matches(shader.FragmentPath, full))
                continue;

            if (shader.TryReload(out var error))
                Console.WriteLine($"[ShaderHotReload] Reloaded shaders using {name}");
            else
                Console.WriteLine($"[ShaderHotReload] Failed to reload {name}: {error}");
        }
    }

    private static bool Matches(string shaderPath, string changedFullPath) =>
        string.Equals(Path.GetFullPath(shaderPath), changedFullPath, StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _watcher.Dispose();
}
