namespace Centauri.Utils.Misc;

public struct FrameStats
{
    // General
    public float FrameTime      { get; set; }
    public float FPS            { get; set; }
    public int   DrawnEntities  { get; set; }
    public int   CulledEntities { get; set; }
    public int   DrawCalls      { get; set; }
    public int   TextureBinds   { get; set; }
    public int   TotalIndices   { get; set; }
    public int   TotalVertices  { get; set; }
    public int TotalEntities => DrawnEntities + CulledEntities;
    
    // Instancing
    public int   Batches        { get; set; }
    public float InstancesPerDraw => DrawCalls > 0 ? (float)DrawnEntities / DrawCalls : 0f;
    
    public int   NaiveDrawCalls { get; set; }   
    public float DrawCallReduction => NaiveDrawCalls > 0 ? (1f - DrawCalls / (float)NaiveDrawCalls) * 100f : 0f;

    public int   RenderableEntities { get; set; }
    public int   TwoSidedEntities   { get; set; }
    public float TwoSidedPercent => RenderableEntities > 0 ? TwoSidedEntities / (float)RenderableEntities * 100f : 0f;
    
    // Shadows
    public int ShadowTotal => ShadowCasters + ShadowCulled;
    public int ShadowCasters { get; set; }   // depth-pass draws, summed across cascades
    public int ShadowCulled  { get; set; }   // frustum-culled per cascade, summed
    
    // Spatial Culling
    public int GridColumns  { get; set; }
    public int GridRows     { get; set; }
    public int GridOccupied { get; set; }    // cells holding at least one entity
    public int GridVisited  { get; set; }    // cells touched by the camera query
    public int GridCells => GridColumns * GridRows;
}