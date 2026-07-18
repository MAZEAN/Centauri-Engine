namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

public sealed class ColorGradingSection : ISection
{
    private readonly ColorGrading _grading;

    public ColorGradingSection(ColorGrading grading) => _grading = grading;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Color Grading", startCollapsed: true);
        if (!s.Open) return;

        Widgets.DragRow("Exposure",    _grading.Exposure,   v => _grading.Exposure   = v,
            0.01f,  0f, 16f, "%.2f", _grading.AuthoredExposure);
        Widgets.DragRow("Black Level", _grading.BlackLevel, v => _grading.BlackLevel = v,
            0.001f, 0f, 0.5f, "%.3f", _grading.AuthoredBlackLevel);
        Widgets.DragRow("Contrast",    _grading.Contrast,   v => _grading.Contrast   = v,
            0.01f,  0f, 2f, "%.2f", _grading.AuthoredContrast);
        Widgets.DragRow("Saturation",  _grading.Saturation, v => _grading.Saturation = v,
            0.01f,  0f, 2f, "%.2f", _grading.AuthoredSaturation);
    }
}