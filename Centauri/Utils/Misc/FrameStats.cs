namespace Centauri.Utils.Misc;

public struct FrameStats
{
    public float FrameTime      { get; set; }
    public float FPS            { get; set; }
    public int TotalEntities => DrawnEntities + CulledEntities;
    public int   DrawnEntities  { get; set; }
    public int   CulledEntities { get; set; }
    public int   DrawCalls      { get; set; }
    public int   TextureBinds   { get; set; }
    public int   TotalIndices   { get; set; }
    public int   TotalVertices  { get; set; }
}