namespace Centauri.Graphics.Resources;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Collections.Generic;

public class GLShader : GLResource
{
    private readonly Dictionary<string, int> _locationCache = new();
    private readonly Dictionary<int, int>       _intCache   = new();
    private readonly Dictionary<int, float>     _floatCache = new();
    private readonly Dictionary<int, Vector2>   _vec2Cache  = new();
    private readonly Dictionary<int, Vector3>   _vec3Cache  = new();
    private readonly Dictionary<int, Vector4>   _vec4Cache  = new();
    private readonly Dictionary<int, Matrix4x4> _matCache   = new();

    private readonly string _vertexPath;
    private readonly string _fragmentPath;

    // Every GLShader that currently exists, regardless of which pass or Material owns it — most
    // shaders in this engine are constructed directly by their owning pass (PostProcessor,
    // ShadowMapper, GTAOPass, ...), not through ResourceSystem's cache, so that cache alone can't
    // find "every shader built from this source file" for ShaderHotReload to reload. Shaders are
    // create-once-live-until-app-exit in this engine (no per-frame churn), so a plain static list
    // — not weak references — is fine: DeleteGL removes the entry the one time a shader actually
    // gets disposed.
    private static readonly List<GLShader> _live = [];
    public static IReadOnlyList<GLShader> Live => _live;

    public string VertexPath   => _vertexPath;
    public string FragmentPath => _fragmentPath;

    public GLShader(GL gl, string vertexPath, string fragmentPath) : base(gl)
    {
        _vertexPath   = vertexPath;
        _fragmentPath = fragmentPath;

        Handle = LinkOrThrow(vertexPath, fragmentPath);
        _live.Add(this);
    }

    private uint LinkOrThrow(string vertexPath, string fragmentPath)
    {
        var vertex   = CompileOrThrow(ShaderType.VertexShader,   vertexPath);
        var fragment = CompileOrThrow(ShaderType.FragmentShader, fragmentPath);

        var program = Gl.CreateProgram();
        Gl.AttachShader(program, vertex);
        Gl.AttachShader(program, fragment);
        Gl.LinkProgram(program);
        Gl.GetProgram(program, GLEnum.LinkStatus, out var status);

        Gl.DetachShader(program, vertex);
        Gl.DetachShader(program, fragment);
        Gl.DeleteShader(vertex);
        Gl.DeleteShader(fragment);

        if (status == 0)
        {
            var log = Gl.GetProgramInfoLog(program);
            Gl.DeleteProgram(program);
            throw new Exception($"Program failed to link with error: {log}");
        }

        return program;
    }

    // Recompiles from the same source paths this shader was created with, swapping the live GL
    // program in place on success — every existing reference to this GLShader instance (held
    // throughout the engine: materials, passes, the shader batcher) picks up the change with no
    // rewiring, since only Handle changes, not the object's identity. On a compile/link error the
    // CURRENT, working program is left completely untouched — a bad edit mid-iteration should
    // degrade to "still showing the last good frame", not crash the running engine or go black.
    // See Utils.Misc.ShaderHotReload, the only caller.
    public bool TryReload(out string? error)
    {
        uint program;
        try
        {
            program = LinkOrThrow(_vertexPath, _fragmentPath);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        Gl.DeleteProgram(Handle);
        Handle = program;
        InvalidateCaches();

        error = null;
        return true;
    }

    public void Use()
    {
        Gl.UseProgram(Handle);
    }

    // Returns false if the location already holds this value (skip the GL call).
    // T : IEquatable<T> -> the strongly typed Equals is used, no boxing.
    private static bool Changed<T>(Dictionary<int, T> cache, int location, T value)
        where T : IEquatable<T>
    {
        if (cache.TryGetValue(location, out var cached) && cached.Equals(value))
            return false;

        cache[location] = value;
        return true;
    }

    public void SetUniform(string name, int value) => SetUniform(GetLocation(name), value);
    public void SetUniform(string name, float value) => SetUniform(GetLocation(name), value);
    public void SetUniform(string name, Vector2 value) => SetUniform(GetLocation(name), value);
    public void SetUniform(string name, Vector3 value) => SetUniform(GetLocation(name), value);
    public void SetUniform(string name, Vector4 value) => SetUniform(GetLocation(name), value);
    public void SetUniform(string name, Matrix4x4 value) => SetUniform(GetLocation(name), value);
    
    public void SetUniform(int location, int value)
    {
        if (location == -1) return;
        if (!Changed(_intCache, location, value)) return;

        Gl.Uniform1(location, value);
    }

    public void SetUniform(int location, float value)
    {
        if (location == -1) return;
        if (!Changed(_floatCache, location, value)) return;

        Gl.Uniform1(location, value);
    }

    public void SetUniform(int location, Vector2 value)
    {
        if (location == -1) return;
        if (!Changed(_vec2Cache, location, value)) return;

        Gl.Uniform2(location, value.X, value.Y);
    }

    public void SetUniform(int location, Vector3 value)
    {
        if (location == -1) return;
        if (!Changed(_vec3Cache, location, value)) return;

        Gl.Uniform3(location, value.X, value.Y, value.Z);
    }

    public void SetUniform(int location, Vector4 value)
    {
        if (location == -1) return;
        if (!Changed(_vec4Cache, location, value)) return;

        Gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    public unsafe void SetUniform(int location, Matrix4x4 value)
    {
        if (location == -1) return;
        if (!Changed(_matCache, location, value)) return;

        Gl.UniformMatrix4(location, 1, false, (float*)&value);
    }

    public unsafe void SetUniformMat3X3(string name, Matrix4x4 m)
    {
        var location = GetLocation(name);
        if (location == -1) return;
        if (!Changed(_matCache, location, m)) return;

        // stackalloc — no heap array per call
        Span<float> mat3 =
        [
            m.M11, m.M12, m.M13,
            m.M21, m.M22, m.M23,
            m.M31, m.M32, m.M33
        ];

        fixed (float* ptr = mat3)
            Gl.UniformMatrix3(location, 1, false, ptr);
    }
    
    public void BindUniformBlock(string blockName, uint bindingPoint)
    {
        var index = Gl.GetUniformBlockIndex(Handle, blockName);
        if (index == uint.MaxValue) return; // GL_INVALID_INDEX

        Gl.UniformBlockBinding(Handle, index, bindingPoint);
    }

    public int GetLocation(string name)
    {
        if (_locationCache.TryGetValue(name, out var cached))
            return cached;

        var location = Gl.GetUniformLocation(Handle, name);
        _locationCache[name] = location; // cache -1 too, so missing uniforms aren't re-queried
        return location;
    }

    private uint CompileOrThrow(ShaderType type, string path)
    {
        var src    = File.ReadAllText(path);
        var handle = Gl.CreateShader(type);

        Gl.ShaderSource(handle, src);
        Gl.CompileShader(handle);

        // GL_COMPILE_STATUS, not "info log is non-empty" — a driver is free to report warnings
        // (deprecated syntax, unused varyings) on a shader that compiled successfully, and the
        // old check treated any such warning as a hard failure. That distinction matters more
        // once TryReload calls this too: a warning-only edit should hot-swap in cleanly, not be
        // reported as a reload error.
        Gl.GetShader(handle, GLEnum.CompileStatus, out var status);
        if (status == 0)
        {
            var infoLog = Gl.GetShaderInfoLog(handle);
            Gl.DeleteShader(handle);
            throw new Exception($"Error compiling shader of type {type} ({path}): {infoLog}");
        }

        return handle;
    }
    
    private void InvalidateCaches()
    {
        _locationCache.Clear();
        _intCache.Clear();
        _floatCache.Clear();
        _vec2Cache.Clear();
        _vec3Cache.Clear();
        _vec4Cache.Clear();
        _matCache.Clear();
    }
    
    protected override void DeleteGL()
    {
        _live.Remove(this);
        Gl.DeleteProgram(Handle);
    }
}