namespace Centauri.UI.Panels;

using ImGuiNET;
using System.Numerics;
using System.Globalization;

using World;
using Config;
using Common;

public class PropertiesPanel
{
    private const float Width   = 300f;
    private const float Padding = 10f;
    private const float BgAlpha = 0.85f;

    private static readonly string[] LightTypes = ["None", "Directional", "Point", "Spot"];
    private static readonly uint[] ShadowSizes = [512, 1024, 2048, 4096, 8192];
    private static readonly string[] ShadowSizeLabels =
        Array.ConvertAll(ShadowSizes, x => x.ToString(CultureInfo.InvariantCulture));

    private readonly ImFontPtr _font;
    private readonly AppConfig _config;
    private readonly ColorGrading _grading;
    
    private Vector3 _euler;            // cached working rotation (deg) for the selected entity
    private bool    _editingRotation;  // true while a rotation axis is being dragged

    private const ImGuiWindowFlags Flags = Widgets.PanelBase;

    public PropertiesPanel(ImFontPtr font, AppConfig config, ColorGrading grading)
    {
        _font    = font;
        _config = config;
        _grading = grading;
    }

    public void Render(Scene scene)
    {
        SetupWindow();

        if (!ImGui.Begin("Properties", Flags))
        {
            ImGui.End();
            return;
        }

        ImGui.PushFont(_font);

        DrawInspectorElements(scene);
        DrawSkybox(scene);
        DrawShadowConfig();
        DrawColorGrading();
        DrawIBLConfig();
        DrawViewport();

        ImGui.PopFont();
        
        ImGui.End();
    }

    private static void SetupWindow()
    {
        var viewport = ImGui.GetMainViewport();

        // stack beneath the outliner: outliner padding + height + a gap
        var top = viewport.WorkPos.Y + Padding + OutlinerPanel.Height + Padding;
        var anchor = new Vector2(viewport.WorkPos.X + viewport.WorkSize.X - Padding, top);

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(1f, 0f));   // pivot top-right
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(Width, 0),
            new Vector2(Width, viewport.WorkPos.Y + viewport.WorkSize.Y - Padding - top));   // fill to bottom edge
        ImGui.SetNextWindowBgAlpha(BgAlpha);
    }

    private void DrawInspectorElements(Scene scene)
    {
        if (scene.Selected is not { } entity)
        {
            ImGui.TextDisabled("No entity selected");
        }
        else
        {
            DrawHeader(entity);                                      // #4
            Widgets.CheckRow("Enabled", entity.Enabled, v => entity.Enabled = v);
            ImGui.Spacing();

            DrawTransform(entity);
            DrawMaterial(entity);
            DrawLight(entity);
        }
    }

    private void DrawTransform(Entity e)
    {
        var open = Widgets.BeginPanel("Transform");
        if (!open)
        {
            Widgets.EndPanel(open); 
            return;
        }
        
        ImGui.PushID("Transform");
        
        var t = e.Transform;
        var a = e.Authored;

        var posReset   = a?.Position ?? Vector3.Zero;
        var rotReset   = a?.Euler    ?? Vector3.Zero;
        var scaleReset = a?.Scale    ?? Vector3.One;

        Widgets.Vec3Rows("Location", t.Position, v => t.Position = v,
            0.05f, "%.3f m", posReset);

        if (!_editingRotation) _euler = t.EulerAngles;
        
        if (Widgets.Vec3Rows("Rotation", ref _euler, 0.5f, "%.1f°", rotReset, out _editingRotation))
            t.SetEulerAngles(_euler.X, _euler.Y, _euler.Z);

        Widgets.Vec3Rows("Scale", t.Scale, v => t.Scale = v,
            0.01f, "%.3f", scaleReset);
        ImGui.PopID();
        
        Widgets.EndPanel(open);
    }

    private static void DrawMaterial(Entity e)
    {
        if (e.Material is not { } mat) return;

        var open = Widgets.BeginPanel("Material");
        if (!open)
        {
            Widgets.EndPanel(open); 
            return;
        }
        
        ImGui.PushID("Material");
        
        Widgets.ColorRow4("Base Color", mat.Color, v => mat.Color = v);
        Widgets.SliderRow("Roughness", mat.RoughnessValue, v => mat.RoughnessValue = v,
            0f, 1f, 0.5f);
        Widgets.SliderRow("Metallic",  mat.MetallicValue,  v => mat.MetallicValue  = v,
            0f, 1f, 0.1f);
        
        ImGui.PopID();
        
        Widgets.EndPanel(open);
    }

    private static void DrawLight(Entity e)
    {
        var open = Widgets.BeginPanel("Light");
        if (!open) { Widgets.EndPanel(open); return; }

        ImGui.PushID("Light");

        var typeIndex = e.Light switch
        {
            DirectionalLight => 1,
            PointLight       => 2,
            SpotLight        => 3,
            _                => 0
        };

        if (Widgets.ComboRow("Type", ref typeIndex, LightTypes))
            e.Light = typeIndex == 0 ? null : CreateLight(typeIndex, e.Light);

        if (e.Light is { } light)
        {
            Widgets.CheckRow("Light Enabled", light.Enabled, v => light.Enabled = v);
            Widgets.ColorRow3("Color", light.Color, v => light.Color = v);            // ## hack no longer needed
            Widgets.DragRow("Intensity", light.Intensity, v => light.Intensity = v,
                0.05f, 0f, 100f, "%.3f", 1f);

            switch (light)
            {
                case DirectionalLight d:
                    Widgets.Vec3Rows("Direction", d.Direction, v => d.Direction = v,
                        0.01f, "%.3f", new Vector3(0f, -1f, 0f));
                    break;
                case SpotLight s:
                    Widgets.Vec3Rows("Direction", s.Direction, v => s.Direction    = v,
                        0.01f, "%.3f", new Vector3(0f, -1f, 0f));
                    Widgets.DragRow("Inner Cutoff", s.InnerCutoff, v => s.InnerCutoff = v,
                        0.5f, 0f, 90f, "%.1f°", 12.5f);
                    Widgets.DragRow("Outer Cutoff", s.OuterCutoff, v => s.OuterCutoff = v,
                        0.5f, 0f, 90f, "%.1f°", 17.5f);
                    break;
                case PointLight p:
                    Widgets.DragRow("Linear",    p.Linear,    v => p.Linear    = v,
                        0.001f, 0f, 1f, "%.3f", 0.09f);
                    Widgets.DragRow("Quadratic", p.Quadratic, v => p.Quadratic = v,
                        0.001f, 0f, 1f, "%.3f", 0.032f);
                    break;
            }
        }

        ImGui.PopID();
        
        Widgets.EndPanel(open);
    }
    
    private static Light CreateLight(int typeIndex, Light? from)
    {
        Light light = typeIndex switch
        {
            1 => new DirectionalLight(),
            2 => new PointLight(),
            3 => new SpotLight(),
            _ => throw new ArgumentOutOfRangeException(nameof(typeIndex))
        };

        if (from is null) return light;

        light.Color     = from.Color;
        light.Intensity = from.Intensity;
        light.Enabled   = from.Enabled;
        
        return light;
    }
    
    private static void DrawSkybox(Scene scene)
    {
        if (scene.Skyboxes.Active is not { } sky) return;   // no skybox loaded
        
        ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);
        
        var open = Widgets.BeginPanel("Skybox");
        if (!open)
        {
            Widgets.EndPanel(open);
            return;
        }

        ImGui.PushID("Skybox");

        if (sky.Texture.IsHdr)
        {
            Widgets.DragRow("Exposure",    sky.Exposure,   v => sky.Exposure   = v,
                0.01f,  0f, 16f, "%.2f", sky.AuthoredExposure);
            Widgets.DragRow("Black Level", sky.BlackLevel, v => sky.BlackLevel = v,
                0.001f, 0f, 0.5f, "%.3f", sky.AuthoredBlackLevel);
        }
        else
        {
            ImGui.TextDisabled("LDR skybox — no HDR controls");
        }

        ImGui.PopID();
        
        Widgets.EndPanel(open);
    }
    
    private void DrawColorGrading()
    {
        ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);
        
        var open = Widgets.BeginPanel("Color Grading");
        if (!open)
        {
            Widgets.EndPanel(open); 
            return;
        }
        
        ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);

        ImGui.PushID("Grading");
        
        Widgets.DragRow("Exposure",    _grading.Exposure,   v => _grading.Exposure   = v,
            0.01f,  0f, 16f,  "%.2f", _grading.AuthoredExposure);
        Widgets.DragRow("Black Level", _grading.BlackLevel, v => _grading.BlackLevel = v,
            0.001f, 0f, 0.5f, "%.3f", _grading.AuthoredBlackLevel);
        Widgets.DragRow("Contrast",    _grading.Contrast,   v => _grading.Contrast   = v,
            0.01f,  0f, 2f,   "%.2f", _grading.AuthoredContrast);
        Widgets.DragRow("Saturation",  _grading.Saturation, v => _grading.Saturation = v, 
            0.01f,  0f, 2f,   "%.2f", _grading.AuthoredSaturation);
        
        ImGui.PopID();

        Widgets.EndPanel(open);
    }
    
    private void DrawIBLConfig()
    {
        ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);
        
        var open = Widgets.BeginPanel("IBL Config");
        if (!open)
        {
            Widgets.EndPanel(open); 
            return;
        }

        var conf = _config.IBLConfig;
        
        ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);

        ImGui.PushID("IBL");
        
        Widgets.DragRow("IBLIntensity", conf.IblIntensity, v => conf.IblIntensity = v,
            0.01f, 0f, 2.0f, "%.3f", conf.AuthoredIblIntensity);
        ImGui.PopID();

        Widgets.EndPanel(open);
    }
    
    private void DrawShadowConfig()
    {
        ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);

        var open = Widgets.BeginPanel("Shadows");
        if (!open)
        {
            Widgets.EndPanel(open);
            return;
        }

        var conf = _config.Shadows;

        ImGui.PushID("Shadows");

        Widgets.CheckRow("Enabled", conf.Enabled, v => conf.Enabled = v);

        Widgets.DragRow("Distance",   conf.Distance,   v => conf.Distance   = v,
            0.5f,   1f, 500f,   "%.1f",   conf.AuthoredDistance);
        Widgets.DragRow("Depth Bias", conf.DepthBias,  v => conf.DepthBias  = v,
            0.0001f, 0f, 0.02f, "%.4f",   conf.AuthoredDepthBias);
        Widgets.DragRow("Normal Bias", conf.NormalBias, v => conf.NormalBias = v,
            0.001f, 0f, 0.2f,   "%.3f",   conf.AuthoredNormalBias);
        
        Widgets.DragRow("PCF Radius", conf.PcfRadius, v => conf.PcfRadius = (int)MathF.Round(v),
            1f, 0f, 4f, "%.0f", conf.AuthoredPcfRadius);

        var sizeIndex = Array.IndexOf(ShadowSizes, conf.Size);
        if (sizeIndex < 0) 
            sizeIndex = 3; // Default 4096
        
        if (Widgets.ComboRow("Map Size", ref sizeIndex, ShadowSizeLabels))
            conf.Size = ShadowSizes[sizeIndex];

        ImGui.TextDisabled($"Near {conf.Near:0.#}   Far {conf.Far:0.#}");

        Widgets.DragRow("Cascades", conf.CascadeCount,
            v => conf.CascadeCount = Math.Clamp((int)MathF.Round(v), 1, conf.MaxCascades),
            1f, 1f, conf.MaxCascades, "%.0f", conf.AuthoredCascadeCount);
        Widgets.DragRow("Split Blend", conf.SplitLambda, v => conf.SplitLambda = v,
            0.01f, 0f, 1f, "%.2f", conf.AuthoredSplitLambda);

        Widgets.CheckRow("Tint Cascades",  conf.DebugCascades,        v => conf.DebugCascades        = v);

        ImGui.PopID();

        Widgets.EndPanel(open);
    }
    
    private void DrawViewport()
    {
        ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);

        var open = Widgets.BeginPanel("Viewport");
        if (!open)
        {
            Widgets.EndPanel(open);
            return;
        }

        var d = _config.Debug;   // reference type — setters mutate the shared instance

        ImGui.PushID("Viewport");

        Widgets.CheckRow("Grid",            d.ShowGrid,          v => d.ShowGrid          = v);
        Widgets.CheckRow("Skybox",          d.ShowSkybox,        v => d.ShowSkybox        = v);
        Widgets.CheckRow("Stats Overlay",   d.ShowStatsOverlay,  v => d.ShowStatsOverlay  = v);
        Widgets.CheckRow("Frustum Culling", d.EnableCulling,     v => d.EnableCulling     = v);

        Widgets.CheckRow("Bounding Boxes",  d.ShowBoundingBoxes, v => d.ShowBoundingBoxes = v);
        Widgets.CheckRow("Cameras",         d.ShowCameras,       v => d.ShowCameras       = v);
        Widgets.CheckRow("Frustums",        d.ShowFrustums,      v => d.ShowFrustums      = v);

        ImGui.PopID();

        Widgets.EndPanel(open);
    }
    
    private static void DrawHeader(Entity e)
    {
        var name = e.Name;
        
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputText("##name", ref name, 64))
            e.Name = name;
        
        ImGui.Spacing();
    }
}