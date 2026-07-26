namespace Centauri.UI;

using Config;
using Layout;

// Facade over the editor's two loosely-related "mode" concepts — Config.Input.Mode (the camera's
// Fly vs. Edit behavior) and EditorWorkspace (which panel set TopBar shows) — so callers that need
// to read or drive either go through one place instead of reaching into AppConfig and TopBar
// independently and reimplementing how the two are supposed to stay in sync. Owns Workspace itself
// (moved out of TopBar, which now just displays whatever this says) since a facade that wrapped only
// one of the two things it manages wouldn't be much of a facade.
internal sealed class ModeManager
{
    private readonly AppConfig _config;
    private ViewMode _lastViewMode;

    public EditorWorkspace Workspace { get; set; } = EditorWorkspace.Viewing;
    public ViewMode ViewMode => _config.Input.Mode;

    public ModeManager(AppConfig config)
    {
        _config       = config;
        _lastViewMode = config.Input.Mode;
    }

    // Called once per frame (UISystem.Render). Tab (Config.Input.ToggleModeKey) is read off the raw
    // keyboard by InputSystem, entirely outside this facade's control, so this polls for the
    // transition rather than being invoked directly by whatever pressed the key — but the *coupling*
    // itself (what workspace a given ViewMode implies) lives in exactly one place, here, instead of
    // smeared across UISystem.Render.
    //
    // Deliberately a deterministic mapping, not a remember/restore of whatever workspace was active
    // before: Fly always means Viewing, Edit always means Edit. A remember/restore version shipped
    // first and made Performance's exit path inconsistent — Tab out of Performance correctly landed
    // on Viewing, but Tab back *restored Performance* instead of landing on Edit, so Tab stopped
    // being the simple "always Edit<->Viewing" toggle it's meant to be. Reaching Performance is a
    // manual act (the P hotkey, or clicking its tab) and so is leaving it via anything other than
    // Tab — Tab itself never lands on it, coming or going.
    public void SyncWithViewMode()
    {
        if (_config.Input.Mode == _lastViewMode) return;

        Workspace     = _config.Input.Mode == ViewMode.Fly ? EditorWorkspace.Viewing : EditorWorkspace.Edit;
        _lastViewMode = _config.Input.Mode;
    }
}
