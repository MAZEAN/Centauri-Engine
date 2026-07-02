namespace Centauri.Rendering.Profiling;

// Smooths FPS/frame-time over a rolling 1-second window so the stats overlay doesn't
// jitter with every single frame's timing noise.
public sealed class FrameTimeTracker
{
    private const float SampleWindowSeconds = 1.0f;

    private float _timer;
    private int   _frameCount;

    public float FPS       { get; private set; }
    public float FrameTime { get; private set; }   // ms

    public void Update(float deltaTime)
    {
        _timer      += deltaTime;
        _frameCount += 1;

        if (_timer < SampleWindowSeconds) return;

        FPS       = _frameCount / _timer;
        FrameTime = 1000f / FPS;
        _timer      = 0f;
        _frameCount = 0;
    }
}
