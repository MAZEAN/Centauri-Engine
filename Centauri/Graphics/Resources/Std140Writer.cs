namespace Centauri.Graphics.Resources;

using System.Numerics;

// Cursor-based writer for std140 buffer layouts. Every write occupies a full vec4 slot
// (vec3s are padded), matching the std140 rule that vec3/vec4 align to 16 bytes — so
// callers express layout as a sequence of slots instead of hand-computed float offsets.
public ref struct Std140Writer
{
    private readonly Span<float> _data;
    private int _cursor;   // in floats

    public Std140Writer(Span<float> data, int startFloat = 0)
    {
        _data   = data;
        _cursor = startFloat;
    }

    // vec3 padded to a full vec4 slot (the 4th component is left as-is, usually zero)
    public void Vec3(Vector3 v)
    {
        _data[_cursor + 0] = v.X;
        _data[_cursor + 1] = v.Y;
        _data[_cursor + 2] = v.Z;
        _cursor += 4;
    }

    public void Vec4(float x, float y, float z, float w)
    {
        _data[_cursor + 0] = x;
        _data[_cursor + 1] = y;
        _data[_cursor + 2] = z;
        _data[_cursor + 3] = w;
        _cursor += 4;
    }
}