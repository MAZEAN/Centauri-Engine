namespace Centauri.Tests.Rendering;

using Centauri.Rendering;

// MaterialRegistry.ReadDefinition (extends-merge + texture-path prefixing) is pure file I/O +
// JSON logic, no GL — and it's the exact machinery behind two real issues from this repo's own
// history: a regression where an entity-level "uvScale" field silently stopped round-tripping
// once it moved to being a .mat-level field (System.Text.Json drops unknown properties with no
// warning), and the "extends"/"path" features added afterward. These tests write real temp .mat
// files and reference them by absolute path — MaterialRegistry.ResolvePath treats any path
// containing '/' as literal (the same escape hatch "Assets/Materials/X.mat"-style references
// use), so this exercises the real merge/prefix logic without needing the repo's actual
// Assets/Materials content (this sandbox doesn't ship any — see CLAUDE.md) or touching
// MaterialRegistry's id-registry scan at all.
public sealed class MaterialRegistryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("centauri-mat-tests-").FullName;
    private readonly MaterialRegistry _registry = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteMat(string fileName, string json)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ReadDefinition_ChildInheritsFieldsItDoesNotSet()
    {
        WriteMat("base.mat", """{ "shader": "Shaders/PBR/shaderPBR", "roughnessScalar": 0.7 }""");
        var childPath = WriteMat("child.mat", $$"""{ "extends": "{{Path.Combine(_dir, "base.mat")}}" }""");

        var def = _registry.ReadDefinition(childPath);

        Assert.Equal(0.7f, def.RoughnessScalar);
    }

    [Fact]
    public void ReadDefinition_ChildOverridesInheritedFields()
    {
        WriteMat("base.mat", """{ "shader": "Shaders/PBR/shaderPBR", "roughnessScalar": 0.7 }""");
        var childPath = WriteMat("child.mat",
            $$"""{ "extends": "{{Path.Combine(_dir, "base.mat")}}", "roughnessScalar": 0.2 }""");

        var def = _registry.ReadDefinition(childPath);

        Assert.Equal(0.2f, def.RoughnessScalar);
    }

    [Fact]
    public void ReadDefinition_MergesThroughAMultiLevelExtendsChain()
    {
        WriteMat("grandparent.mat", """{ "metallicScalar": 0.9 }""");
        var parentPath = WriteMat("parent.mat",
            $$"""{ "extends": "{{Path.Combine(_dir, "grandparent.mat")}}", "roughnessScalar": 0.4 }""");
        var childPath = WriteMat("child.mat", $$"""{ "extends": "{{parentPath}}" }""");

        var def = _registry.ReadDefinition(childPath);

        // Neither child nor parent sets metallicScalar — must come from the grandparent two
        // levels up, not just the immediate parent.
        Assert.Equal(0.9f, def.MetallicScalar);
        Assert.Equal(0.4f, def.RoughnessScalar);
    }

    [Fact]
    public void ReadDefinition_DetectsADirectInheritanceCycle()
    {
        var aPath = Path.Combine(_dir, "a.mat");
        var bPath = Path.Combine(_dir, "b.mat");
        WriteMat("a.mat", $$"""{ "extends": "{{bPath}}" }""");
        WriteMat("b.mat", $$"""{ "extends": "{{aPath}}" }""");

        var exception = Record.Exception(() => _registry.ReadDefinition(aPath));

        Assert.NotNull(exception);
        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadDefinition_DetectsASelfReferencingCycle()
    {
        var path = Path.Combine(_dir, "self.mat");
        WriteMat("self.mat", $$"""{ "extends": "{{path}}" }""");

        var exception = Record.Exception(() => _registry.ReadDefinition(path));

        Assert.NotNull(exception);
        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ApplyTexturePathPrefix always concatenates with a literal '/' (matching every hand-authored
    // asset path in this codebase, e.g. "Testing/CorrugatedIron/...") regardless of OS — so the
    // expected strings here are built the same way, not via Path.Combine (which would use '\' on
    // Windows and silently mismatch what the production code actually produces).
    [Fact]
    public void ReadDefinition_PrefixesBareTextureFilenamesWithThePathField()
    {
        var texturesDir = _dir.Replace('\\', '/') + "/Textures/Bark";
        var path = WriteMat("child.mat",
            $$"""{ "path": "{{texturesDir}}", "albedo": "diff.jpg", "normal": "nor.jpg" }""");

        var def = _registry.ReadDefinition(path);

        Assert.Equal(texturesDir + "/diff.jpg", def.Albedo);
        Assert.Equal(texturesDir + "/nor.jpg", def.Normal);
    }

    [Fact]
    public void ReadDefinition_LeavesAlreadyQualifiedTexturePathsUntouched()
    {
        var basePath = _dir.Replace('\\', '/');
        var explicitAlbedo = basePath + "/Elsewhere/diff.jpg";
        var path = WriteMat("child.mat",
            $$"""{ "path": "{{basePath}}", "albedo": "{{explicitAlbedo}}" }""");

        var def = _registry.ReadDefinition(path);

        // The albedo value already contains a path separator, so it's a literal path already —
        // must be left exactly as authored, not re-prefixed with the "path" field's directory.
        Assert.Equal(explicitAlbedo, def.Albedo);
    }

    [Fact]
    public void ReadDefinition_DoesNotTouchTexturePathsWhenPathFieldIsAbsent()
    {
        var path = WriteMat("child.mat", """{ "albedo": "diff.jpg" }""");

        var def = _registry.ReadDefinition(path);

        Assert.Equal("diff.jpg", def.Albedo);
    }
}
