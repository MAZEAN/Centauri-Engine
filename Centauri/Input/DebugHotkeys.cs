namespace Centauri.Input;

using Silk.NET.Input;

using Config;
using World;
using World.Components;

// The scene/debug hotkeys (M/C/B/N/G) — everything OnKeyDown falls through to once Escape and
// the fly/edit mode toggle are handled. Kept separate from InputSystem so that class stays
// about translating raw input into camera movement/picking, not dispatching feature toggles.
internal sealed class DebugHotkeys
{
    private readonly AppConfig _config;
    private readonly Scene _scene;
    private readonly Action _resetActiveController;

    public DebugHotkeys(AppConfig config, Scene scene, Action resetActiveController)
    {
        _config = config;
        _scene = scene;
        _resetActiveController = resetActiveController;
    }

    public void Handle(Key key)
    {
        switch (key)
        {
            case Key.M: _config.Debug.ToggleShowStatsOverlay(); 
                break;
            case Key.C: _scene.Cameras.Cycle(); _resetActiveController();
                break;
            case Key.B: _scene.Skyboxes.Cycle();
                break;
            case Key.N: _scene.FindComponent<DayNightCycle>()?.Toggle();
                break;
            case Key.G: _config.Debug.CycleShading();
                break;
        }
    }
}