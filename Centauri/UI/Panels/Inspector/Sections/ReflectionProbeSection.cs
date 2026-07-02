namespace Centauri.UI.Panels.Inspector.Sections;

using System.Numerics;
using ImGuiNET;

using Config;
using World;
using Common;

public sealed class ReflectionProbeSection : ISection
{
    private readonly AppConfig _config;

    public ReflectionProbeSection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Reflection Probe", startCollapsed: true);
        if (!s.Open) return;

        var conf = _config.ReflectionProbe;

        Widgets.CheckRow("Enabled", conf.Enabled, v => conf.Enabled = v);

        Widgets.Vec3Rows("Position",
            new Vector3(conf.Position[0], conf.Position[1], conf.Position[2]),
            v =>
            {
                conf.Position[0] = v.X;
                conf.Position[1] = v.Y;
                conf.Position[2] = v.Z;
            },
            0.1f, "%.2f", new Vector3(0f, 2f, -3f));

        Widgets.DragRow("Resolution", conf.Resolution,
            v => conf.Resolution = (uint)Math.Clamp((int)MathF.Round(v), 32, 512),
            1f, 32f, 512f, "%.0f");

        Widgets.DragRow("Intensity", conf.Intensity, v => conf.Intensity = v,
            0.01f, 0f, 4f, "%.2f", conf.AuthoredIntensity);
        Widgets.Vec3Rows("Box Center",
            new Vector3(conf.BoxCenter[0], conf.BoxCenter[1], conf.BoxCenter[2]),
            v => { conf.BoxCenter[0] = v.X; conf.BoxCenter[1] = v.Y; conf.BoxCenter[2] = v.Z; },
            0.1f, "%.2f", new Vector3(0f, -1f, -3f));

        Widgets.Vec3Rows("Box Size",
            new Vector3(conf.BoxSize[0], conf.BoxSize[1], conf.BoxSize[2]),
            v => { conf.BoxSize[0] = v.X; conf.BoxSize[1] = v.Y; conf.BoxSize[2] = v.Z; },
            0.1f, "%.2f", new Vector3(20f, 3f, 20f));

        Widgets.DragRow("Box Falloff", conf.BoxFalloff, v => conf.BoxFalloff = v,
            0.05f, 0.01f, 20f, "%.2f", conf.AuthoredBoxFalloff);

        ImGui.TextDisabled(conf.Baked ? "Baked" : "Not baked");

        // Bakes are one-shot (see ReflectionProbeBaker) — position/resolution edits above
        // only take effect once this is clicked, they don't trigger a live re-bake.
        if (ImGui.Button("Rebake"))
            conf.RebakeRequested = true;
    }
}