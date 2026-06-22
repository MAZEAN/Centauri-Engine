namespace Centauri.Graphics.Resources;

using Silk.NET.OpenGL;

// Base for objects that own a single GL handle. Centralizes the (idempotent) dispose
// pattern so subclasses only declare how their handle is deleted via DeleteGL().
public abstract class GLResource : IDisposable
{
    protected readonly GL Gl;

    public uint Handle { get; protected set; }

    private bool _disposed;

    protected GLResource(GL gl) => Gl = gl;

    // Delete the underlying GL object(s). Called at most once.
    protected abstract void DeleteGL();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DeleteGL();
        GC.SuppressFinalize(this);
    }
}
