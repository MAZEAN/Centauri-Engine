namespace Centauri.Tests.Shadows;

using System.Numerics;
using Silk.NET.Maths;

using Centauri.Config;
using Centauri.World;
using Centauri.Rendering.Shadows;
using Centauri.Utils.Geometry;

// CascadeBuilder is pure CPU math (System.Numerics only, no GL) — see CLAUDE.md's own note on
// this being the standard example for the "no GL context needed" test harness pattern. These
// exercise the invariants CascadeBuilder's own comments call out as the things past bugs broke:
// determinism (a "stable fit" that isn't bit-identical for identical inputs isn't stable),
// monotonic splits, and the array-reuse optimization.
public class CascadeBuilderTests
{
    private static Camera MakeCamera(Vector3 position)
    {
        var camera = new Camera(new CameraConfig(), "Test", position, Vector3.UnitY, yaw: -90f, pitch: 0f);
        camera.SetAspectRatio(new Vector2D<int>(1920, 1080));
        return camera;
    }

    private static readonly BoundingBox SceneBounds = new(new Vector3(-10f, -1f, -10f), new Vector3(10f, 1f, 10f));
    private static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.3f, -1f, -0.4f));

    private static float FixedResolution(int _) => 2048f;

    [Fact]
    public void Build_ReturnsOneCascadePerConfiguredCount()
    {
        var config = new AppConfig();
        config.Shadows.CascadeCount = 3;
        var builder = new CascadeBuilder(config);

        var cascades = builder.Build(MakeCamera(Vector3.Zero), SunDirection, SceneBounds, FixedResolution, []);

        Assert.Equal(3, cascades.Length);
    }

    [Theory]
    [InlineData(0, 1)]   // below the valid range
    [InlineData(99, 4)]  // above MaxCascades (4)
    public void Build_ClampsCascadeCountToValidRange(int requested, int expectedCount)
    {
        var config = new AppConfig();
        config.Shadows.CascadeCount = requested;
        var builder = new CascadeBuilder(config);

        var cascades = builder.Build(MakeCamera(Vector3.Zero), SunDirection, SceneBounds, FixedResolution, []);

        Assert.Equal(expectedCount, cascades.Length);
    }

    [Fact]
    public void Build_ProducesStrictlyIncreasingSplitDepths()
    {
        var config = new AppConfig();
        config.Shadows.CascadeCount = 4;
        var builder = new CascadeBuilder(config);

        var cascades = builder.Build(MakeCamera(Vector3.Zero), SunDirection, SceneBounds, FixedResolution, []);

        for (var i = 1; i < cascades.Length; i++)
            Assert.True(cascades[i].SplitDepth > cascades[i - 1].SplitDepth,
                $"cascade {i} split ({cascades[i].SplitDepth}) should exceed cascade {i - 1}'s ({cascades[i - 1].SplitDepth})");
    }

    [Fact]
    public void Build_ReusesArrayInstanceWhenLengthMatches()
    {
        var config = new AppConfig();
        var builder = new CascadeBuilder(config);
        var reuse = new Cascade[Math.Clamp(config.Shadows.CascadeCount, 1, 4)];

        var result = builder.Build(MakeCamera(Vector3.Zero), SunDirection, SceneBounds, FixedResolution, reuse);

        Assert.Same(reuse, result);
    }

    [Fact]
    public void Build_AllocatesFreshArrayWhenReuseLengthMismatches()
    {
        var config = new AppConfig();
        var builder = new CascadeBuilder(config);
        var wrongSize = new Cascade[1];

        var result = builder.Build(MakeCamera(Vector3.Zero), SunDirection, SceneBounds, FixedResolution, wrongSize);

        Assert.NotSame(wrongSize, result);
    }

    // The whole point of CascadeBuilder's texel/Z snapping (see its own extensive comments on the
    // fixed-origin-view bug it fixes) is that an identical camera+light+scene state always
    // produces an identical fitted matrix — anything else means some quantity in the fit is
    // drifting off floating-point noise instead of snapping to a stable grid.
    [Fact]
    public void Build_IsDeterministicForIdenticalInputs()
    {
        var config = new AppConfig();
        var builder = new CascadeBuilder(config);

        var first  = builder.Build(MakeCamera(new Vector3(1.234f, 2.5f, -3.75f)), SunDirection, SceneBounds, FixedResolution, []);
        var second = builder.Build(MakeCamera(new Vector3(1.234f, 2.5f, -3.75f)), SunDirection, SceneBounds, FixedResolution, []);

        for (var i = 0; i < first.Length; i++)
        {
            Assert.Equal(first[i].Matrix, second[i].Matrix);
            Assert.Equal(first[i].Radius, second[i].Radius);
            Assert.Equal(first[i].DepthRange, second[i].DepthRange);
        }
    }

    [Fact]
    public void Build_NeverProducesNonFiniteMatrices()
    {
        var config = new AppConfig();
        var builder = new CascadeBuilder(config);

        var cascades = builder.Build(MakeCamera(Vector3.Zero), SunDirection, SceneBounds, FixedResolution, []);

        foreach (var c in cascades)
        {
            var m = c.Matrix;
            Assert.True(float.IsFinite(m.M11) && float.IsFinite(m.M22) && float.IsFinite(m.M33) && float.IsFinite(m.M44),
                "cascade matrix contains a non-finite component");
            Assert.True(float.IsFinite(c.Radius) && c.Radius > 0f);
        }
    }
}
