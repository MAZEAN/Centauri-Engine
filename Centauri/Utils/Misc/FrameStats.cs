namespace Centauri.Utils.Misc;

public struct FrameStats
{
    public float FrameTime     { get; set; } // ms
    public float FPS           { get; set; }
    public int   TotalEntities { get; set; }
    public int   DrawnEntities { get; set; }
    public int   CulledEntities => TotalEntities - DrawnEntities;
    public int   DrawCalls      { get; set; }
    public int   TextureBinds   { get; set; }
    public bool  FrustumRebuilt { get; set; }
}