namespace Centauri.Graphics.Resources;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Collections.Generic;

public class GLShader : GLResource
{
    // name -> uniform location (hashed once per name, then reused)
    private readonly Dictionary<string, int> _locationCache = new();

    // last uploaded value per location, split by type so comparisons are
    // strongly typed (IEquatable<T>) and never box. Keyed by int location
    // rather than string, so the per-frame value check does no string hashing.
    private readonly Dictionary<int, int>       _intCache   = new();
    private readonly Dictionary<int, float>     _floatCache = new();
    private readonly Dictionary<int, Vector2>   _vec2Cache  = new();
    private readonly Dictionary<int, Vector3>   _vec3Cache  = new();
    private readonly Dictionary<int, Vector4>   _vec4Cache  = new();
    private readonly Dictionary<int, Matrix4x4> _matCache   = new();

    public GLShader(GL gl, string vertexPath, string fragmentPath) : base(gl)
    {
        var vertex   = LoadShader(ShaderType.VertexShader,   vertexPath);
        var fragment = LoadShader(ShaderType.FragmentShader, fragmentPath);

        Handle = Gl.CreateProgram();
        Gl.AttachShader(Handle, vertex);
        Gl.AttachShader(Handle, fragment);
        Gl.LinkProgram(Handle);
        Gl.GetProgram(Handle, GLEnum.LinkStatus, out var status);

        if (status == 0)
            throw new Exception($"Program failed to link with error: {Gl.GetProgramInfoLog(Handle)}");

        Gl.DetachShader(Handle, vertex);
        Gl.DetachShader(Handle, fragment);
        Gl.DeleteShader(vertex);
        Gl.DeleteShader(fragment);
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

    public void SetUniform(string name, int value)
    {
        var location = GetLocation(name);
        if (location == -1) return;
        if (!Changed(_intCache, location, value)) return;

        Gl.Uniform1(location, value);
    }

    public void SetUniform(string name, float value)
    {
        var location = GetLocation(name);
        if (location == -1) return;
        if (!Changed(_floatCache, location, value)) return;

        Gl.Uniform1(location, value);
    }

    public void SetUniform(string name, float x, float y)
    {
        var location = GetLocation(name);
        if (location == -1) return;
        if (!Changed(_vec2Cache, location, new Vector2(x, y))) return;

        Gl.Uniform2(location, x, y);
    }

    public void SetUniform(string name, Vector2 value)
    {
        var location = GetLocation(name);
        if (location == -1) return;
        if (!Changed(_vec2Cache, location, value)) return;

        Gl.Uniform2(location, value.X, value.Y);
    }

    public void SetUniform(string name, Vector3 value)
    {
        var location = GetLocation(name);
        if (location == -1) return;
        if (!Changed(_vec3Cache, location, value)) return;

        Gl.Uniform3(location, value.X, value.Y, value.Z);
    }

    public void SetUniform(string name, Vector4 value)
    {
        var location = GetLocation(name);
        if (location == -1) return;
        if (!Changed(_vec4Cache, location, value)) return;

        Gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    public unsafe void SetUniform(string name, Matrix4x4 value)
    {
        var location = GetLocation(name);
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

    private int GetLocation(string name)
    {
        if (_locationCache.TryGetValue(name, out var cached))
            return cached;

        var location = Gl.GetUniformLocation(Handle, name);
        _locationCache[name] = location; // cache -1 too, so missing uniforms aren't re-queried
        return location;
    }

    private uint LoadShader(ShaderType type, string path)
    {
        var src    = File.ReadAllText(path);
        var handle = Gl.CreateShader(type);

        Gl.ShaderSource(handle, src);
        Gl.CompileShader(handle);

        var infoLog = Gl.GetShaderInfoLog(handle);
        if (!string.IsNullOrWhiteSpace(infoLog))
            throw new Exception($"Error compiling shader of type {type}, failed with error {infoLog}");

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
        Gl.DeleteProgram(Handle);
    }
}