namespace Centauri.Rendering.Resources;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Collections.Generic;

public class GLShader : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    
    // cache last uploaded value per uniform name
    private readonly Dictionary<string, object> _uniformCache = new();
    private readonly Dictionary<string, int> _locationCache = new();
    
    public GLShader(GL gl, string vertexPath, string fragmentPath)
    {
        _gl = gl;

        uint vertex = LoadShader(ShaderType.VertexShader, vertexPath);
        uint fragment = LoadShader(ShaderType.FragmentShader, fragmentPath);
        
        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);
        _gl.GetProgram(_handle, GLEnum.LinkStatus, out var status);
        
        if (status == 0)
        {
            throw new Exception($"Program failed to link with error: {_gl.GetProgramInfoLog(_handle)}");
        }
        
        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
    }

    public void Use()
    {
        _gl.UseProgram(_handle);
    }

    private bool HasChanged(string name, object value)
    {
        if (_uniformCache.TryGetValue(name, out var cached) && cached.Equals(value))
            return false;
        _uniformCache[name] = value;
        return true;
    }

    // existing SetUniform overloads — wrap each with cache check
    public void SetUniform(string name, int value)
    {
        if (!HasChanged(name, value)) return;
        int location = GetLocation(name);
        _gl.Uniform1(location, value);
    }

    public void SetUniform(string name, float value)
    {
        if (!HasChanged(name, value)) return;
        int location = GetLocation(name);
        _gl.Uniform1(location, value);
    }

    public void SetUniform(string name, float x, float y)
    {
        // box as tuple for cache comparison
        var key = (x, y);
        if (!HasChanged(name, key)) return;
        int location = GetLocation(name);
        _gl.Uniform2(location, x, y);
    }

    public void SetUniform(string name, Vector2 value)
    {
        if (!HasChanged(name, value)) return;
        int location = GetLocation(name);
        _gl.Uniform2(location, value.X, value.Y);
    }

    public void SetUniform(string name, Vector3 value)
    {
        if (!HasChanged(name, value)) return;
        int location = GetLocation(name);
        _gl.Uniform3(location, value.X, value.Y, value.Z);
    }

    public void SetUniform(string name, Vector4 value)
    {
        if (!HasChanged(name, value)) return;
        int location = GetLocation(name);
        _gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    public void SetUniform(string name, Matrix4x4 value)
    {
        if (!HasChanged(name, value)) return;
        int location = GetLocation(name);
        unsafe { _gl.UniformMatrix4(location, 1, false, (float*)&value); }
    }

    public unsafe void SetUniformMat3x3(string name, Matrix4x4 m)
    {
        if (!HasChanged(name, m)) return;
        int location = GetLocation(name);
        float[] mat3 =
        [
            m.M11, m.M12, m.M13,
            m.M21, m.M22, m.M23,
            m.M31, m.M32, m.M33
        ];
        fixed (float* ptr = mat3)
            _gl.UniformMatrix3(location, 1, false, ptr);
    }

    private int GetLocation(string name)
    {
        if (_locationCache.TryGetValue(name, out var cached))
            return cached;

        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
            throw new Exception($"Uniform '{name}' not found in shader.");

        _locationCache[name] = location;
        return location;
    }
    
    public void Dispose()
    {
        _gl.DeleteProgram(_handle);
    }

    private uint LoadShader(ShaderType type, string path)
    {
        // Console.WriteLine($"Loading shader: {Path.GetFullPath(path)}");
        string src = File.ReadAllText(path);
        uint handle = _gl.CreateShader(type);
        
        _gl.ShaderSource(handle, src);
        _gl.CompileShader(handle);
        
        string infoLog = _gl.GetShaderInfoLog(handle);
        
        if (!string.IsNullOrWhiteSpace(infoLog))
        {
            throw new Exception($"Error compiling shader of type {type}, failed with error {infoLog}");
        }

        return handle;
    }
}