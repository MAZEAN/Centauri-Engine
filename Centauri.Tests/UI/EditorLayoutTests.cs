namespace Centauri.Tests.UI;

using System.Numerics;

using Centauri.UI.Layout;

// EditorLayout is pure geometry (no ImGui, no GL, no state), and it's exactly the kind of logic
// that silently breaks at a resolution nobody happened to test by hand — a stray independent
// recomputation of a shared edge, an unclamped subtraction going negative at a small window size.
// These pin two invariants across a real spread of resolutions/aspect ratios/UI scales: the Edit
// workspace's five regions tile the work area *exactly* (no gap, no overlap, no region out of
// bounds), and every workspace degrades to non-negative sizes instead of throwing or going negative
// even at pathologically small windows.
public sealed class EditorLayoutTests
{
    // A spread of real and edge-case resolutions/aspect ratios, plus non-zero work-area origins
    // (simulating e.g. a future global menu bar shifting the main viewport down) and a few UI
    // scales (Widgets.FontScale varies with the configured font size).
    public static IEnumerable<object[]> Resolutions()
    {
        (float w, float h)[] sizes =
        [
            (1024f, 768f), (1280f, 720f), (1366f, 768f), (1600f, 900f),
            (1920f, 1080f), (2560f, 1440f), (3840f, 2160f),
            (900f, 1440f),   // portrait-ish / rotated monitor
            (640f, 480f),    // small
        ];
        Vector2[] origins = [Vector2.Zero, new Vector2(0f, 24f)];
        float[] scales = [0.75f, 1f, 1.5f, 2f];

        foreach (var (w, h) in sizes)
        foreach (var origin in origins)
        foreach (var scale in scales)
            yield return [w, h, origin, scale];
    }

    private const float Eps = 1e-3f;

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void Edit_TilesTheWorkAreaExactly_NoGapsNoOverlaps(float w, float h, Vector2 origin, float scale)
    {
        var size   = new Vector2(w, h);
        var r      = EditorLayout.Compute(EditorWorkspace.Edit, origin, size, scale);

        Assert.NotNull(r.LeftTools);
        Assert.NotNull(r.Outliner);
        Assert.NotNull(r.Properties);
        var leftTools = r.LeftTools!.Value;
        var outliner  = r.Outliner!.Value;
        var properties = r.Properties!.Value;

        // Every region inside the work area.
        AssertWithinBounds(r.TopBar, origin, size);
        AssertWithinBounds(leftTools, origin, size);
        AssertWithinBounds(r.Viewport, origin, size);
        AssertWithinBounds(outliner, origin, size);
        AssertWithinBounds(properties, origin, size);

        // Shared edges are the *same* value (construction, not coincidence — see EditorLayout's
        // own comment on why regions share intermediate variables instead of each recomputing).
        Assert.Equal(r.TopBar.Bottom, leftTools.Top, Eps);
        Assert.Equal(r.TopBar.Bottom, r.Viewport.Top, Eps);
        Assert.Equal(r.TopBar.Bottom, outliner.Top, Eps);
        Assert.Equal(leftTools.Right, r.Viewport.Left, Eps);
        Assert.Equal(r.Viewport.Right, outliner.Left, Eps);
        Assert.Equal(r.Viewport.Right, properties.Left, Eps);
        Assert.Equal(outliner.Bottom, properties.Top, Eps);
        Assert.Equal(outliner.Right, properties.Right, Eps);
        Assert.Equal(outliner.Left, properties.Left, Eps);

        // Every region reaches the far edges of the work area (no strip of dead space at the
        // bottom/right that isn't covered by anything).
        Assert.Equal(origin.X, r.TopBar.Left, Eps);
        Assert.Equal(origin.X + w, r.TopBar.Right, Eps);
        Assert.Equal(origin.X, leftTools.Left, Eps);
        Assert.Equal(origin.X + w, properties.Right, Eps);
        Assert.Equal(origin.Y + h, leftTools.Bottom, Eps);
        Assert.Equal(origin.Y + h, r.Viewport.Bottom, Eps);
        Assert.Equal(origin.Y + h, properties.Bottom, Eps);

        // Area conservation: the five regions' areas sum to exactly the work area. Combined with
        // the exact-shared-edge assertions above, this rules out both an overlap (sum too big) and
        // a gap (sum too small) that the edge checks alone wouldn't catch (e.g. a region shifted by
        // the same amount on both sides).
        var totalArea = w * h;
        var sumArea = Area(r.TopBar) + Area(leftTools) + Area(r.Viewport) + Area(outliner) + Area(properties);
        Assert.Equal(totalArea, sumArea, MathF.Max(1f, totalArea * 1e-4f));
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AllWorkspaces_NeverProduceNegativeSizes(float w, float h, Vector2 origin, float scale)
    {
        foreach (var workspace in Enum.GetValues<EditorWorkspace>())
        {
            var r = EditorLayout.Compute(workspace, origin, new Vector2(w, h), scale);

            AssertNonNegative(r.TopBar);
            AssertNonNegative(r.Viewport);
            if (r.LeftTools is { } lt) AssertNonNegative(lt);
            if (r.Outliner is { } o) AssertNonNegative(o);
            if (r.Properties is { } p) AssertNonNegative(p);
            if (r.Stats is { } s) AssertNonNegative(s);
            if (r.Performance is { } perf) AssertNonNegative(perf);
        }
    }

    [Theory]
    [InlineData(1280f, 720f)]
    [InlineData(1920f, 1080f)]
    public void Performance_StatsAndGraphsTileTheBodyExactly(float w, float h)
    {
        var r = EditorLayout.Compute(EditorWorkspace.Performance, Vector2.Zero, new Vector2(w, h), 1f);

        Assert.NotNull(r.Stats);
        Assert.NotNull(r.Performance);
        var stats = r.Stats!.Value;
        var perf  = r.Performance!.Value;

        Assert.Equal(stats.Top, perf.Top, Eps);
        Assert.Equal(stats.Bottom, perf.Bottom, Eps);
        Assert.Equal(stats.Right, perf.Left, Eps);
        Assert.Equal(0f, stats.Left, Eps);
        Assert.Equal(w, perf.Right, Eps);

        var bodyH = h - r.TopBar.Size.Y;
        Assert.Equal(w * bodyH, stats.Size.X * stats.Size.Y + perf.Size.X * perf.Size.Y, MathF.Max(1f, w * bodyH * 1e-4f));
    }

    [Fact]
    public void Viewing_IsJustTheTopBarAndAFullWidthViewport()
    {
        var r = EditorLayout.Compute(EditorWorkspace.Viewing, Vector2.Zero, new Vector2(1920f, 1080f), 1f);

        Assert.Null(r.LeftTools);
        Assert.Null(r.Outliner);
        Assert.Null(r.Properties);
        Assert.Null(r.Stats);
        Assert.Null(r.Performance);

        Assert.Equal(1920f, r.TopBar.Size.X, Eps);
        Assert.Equal(1920f, r.Viewport.Size.X, Eps);
        Assert.Equal(r.TopBar.Bottom, r.Viewport.Top, Eps);
        Assert.Equal(1080f, r.Viewport.Bottom, Eps);
    }

    [Fact]
    public void Edit_AtAPathologicallySmallResolution_StillFitsWithoutNegativeViewport()
    {
        // Smaller than the tool column + sidebar's design widths combined.
        var r = EditorLayout.Compute(EditorWorkspace.Edit, Vector2.Zero, new Vector2(64f, 64f), 1f);

        Assert.NotNull(r.LeftTools);
        Assert.NotNull(r.Outliner);
        Assert.NotNull(r.Properties);
        Assert.True(r.Viewport.Size.X >= 0f);
        Assert.True(r.Viewport.Size.Y >= 0f);
        Assert.True(r.LeftTools!.Value.Size.X >= 0f);
        Assert.True(r.Outliner!.Value.Size.X >= 0f);
    }

    private static void AssertWithinBounds(LayoutRect rect, Vector2 origin, Vector2 size)
    {
        Assert.True(rect.Left >= origin.X - Eps, $"{rect} starts left of the work area origin");
        Assert.True(rect.Top >= origin.Y - Eps, $"{rect} starts above the work area origin");
        Assert.True(rect.Right <= origin.X + size.X + Eps, $"{rect} extends past the right edge");
        Assert.True(rect.Bottom <= origin.Y + size.Y + Eps, $"{rect} extends past the bottom edge");
    }

    private static void AssertNonNegative(LayoutRect rect)
    {
        Assert.True(rect.Size.X >= -Eps, $"{rect} has negative width");
        Assert.True(rect.Size.Y >= -Eps, $"{rect} has negative height");
    }

    private static float Area(LayoutRect r) => r.Size.X * r.Size.Y;
}
