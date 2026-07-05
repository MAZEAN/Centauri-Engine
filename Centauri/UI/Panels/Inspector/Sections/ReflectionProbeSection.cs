namespace Centauri.UI.Panels.Inspector.Sections;

using System.Numerics;
using ImGuiNET;

using Config;
using World;
using Common;

public sealed class ReflectionProbeSection : ISection
{
    private readonly ReflectionProbeConfig _config;

    public ReflectionProbeSection(ReflectionProbeConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Reflection Probe", startCollapsed: true);
        if (!s.Open) return;
        
        Widgets.CheckRow("Enabled", _config.Enabled, v => _config.Enabled = v);

        Widgets.Vec3Rows("Position",
            new Vector3(_config.Position[0], _config.Position[1], _config.Position[2]),
            v =>
            {
                _config.Position[0] = v.X;
                _config.Position[1] = v.Y;
                _config.Position[2] = v.Z;
            },
            0.1f, "%.2f", new Vector3(0f, 2f, -3f));

        Widgets.DragRow("Resolution", _config.Resolution,
            v => _config.Resolution = (uint)Math.Clamp((int)MathF.Round(v), 32, 512),
            1f, 32f, 512f, "%.0f");

        Widgets.DragRow("Intensity", _config.Intensity, v => _config.Intensity = v,
            0.01f, 0f, 4f, "%.2f", _config.AuthoredIntensity);
        Widgets.Vec3Rows("Box Center",
            new Vector3(_config.BoxCenter[0], _config.BoxCenter[1], _config.BoxCenter[2]),
            v => { _config.BoxCenter[0] = v.X; _config.BoxCenter[1] = v.Y; _config.BoxCenter[2] = v.Z; },
            0.1f, "%.2f", new Vector3(0f, -1f, -3f));

        Widgets.Vec3Rows("Box Size",
            new Vector3(_config.BoxSize[0], _config.BoxSize[1], _config.BoxSize[2]),
            v => { _config.BoxSize[0] = v.X; _config.BoxSize[1] = v.Y; _config.BoxSize[2] = v.Z; },
            0.1f, "%.2f", new Vector3(20f, 3f, 20f));

        Widgets.DragRow("Box Falloff", _config.BoxFalloff, v => _config.BoxFalloff = v,
            0.05f, 0.01f, 20f, "%.2f", _config.AuthoredBoxFalloff);

        ImGui.TextDisabled(_config.Baked ? "Baked" : "Not baked");
        
        if (ImGui.Button("Rebake"))
            _config.RebakeRequested = true;
    }
}