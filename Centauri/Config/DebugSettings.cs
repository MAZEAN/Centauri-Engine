namespace Centauri.Config;

public class DebugSettings
{
    public bool EnableCulling      { get; private set; } = true;
    public bool ShowDebugView      { get; private set; } = false;
    public bool ShowBoundingBoxes  { get; private set; } = false;
    public bool ShowFrustums       { get; private set; } = false;
    public bool ShowCameras        { get; private set; } = false;
    
    public bool ShowGrid           { get; private set; } = true;

    public void ToggleShowDebugView()
    {
        ShowDebugView = !ShowDebugView;
        
        // Show all when toggling to debug view
        if (ShowDebugView)
        {
            ShowBoundingBoxes = true;
            ShowFrustums = true;
            ShowCameras = true;
        }
    }
    public void ToggleEnableCulling () => EnableCulling  = !EnableCulling ;
    public void ToggleShowBoundingBoxes() => ShowBoundingBoxes = !ShowBoundingBoxes;
    public void ToggleShowFrustums() => ShowFrustums = !ShowFrustums;
    public void ToggleShowCameras() => ShowCameras = !ShowCameras;
    public void ToggleShowGrid() => ShowGrid = !ShowGrid;
}